// The managed client (Packages/com.hawkes.net/Runtime/HawkesNetClient.cs) end to end, with plain dotnet, against a Hawkes
// Net service: two players in a test room exchange JSON and text messages and poses, one goes away and the host role
// moves, a third joins and hears who is away.
//   dotnet run --project DevTools/ManagedTest -- [endpoint, default http://127.0.0.1:8786] [project, default netdemo]
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hawkes;

static class Program
{
    static int failed;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

    static List<Dictionary<string, object>> Drain(HawkesNetClient c, List<Dictionary<string, object>> into)
    {
        while (c.Events.TryDequeue(out var e)) into.Add((Dictionary<string, object>)NetJson.Parse(e));
        return into;
    }

    static bool Until(Func<bool> test, int ms = 8000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { if (test()) return true; Thread.Sleep(50); }
        return false;
    }

    static int Main(string[] args)
    {
        var endpoint = args.Length > 0 ? args[0] : "http://127.0.0.1:8786";
        var project = args.Length > 1 ? args[1] : "netdemo";
        var room = "t-unity-managed-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        HawkesNetClient Make(int h, string name) => new HawkesNetClient(h, room, new HawkesNetClientOptions { project = project, endpoint = endpoint, name = name });
        var ea = new List<Dictionary<string, object>>(); var eb = new List<Dictionary<string, object>>(); var ec = new List<Dictionary<string, object>>();
        var a = Make(1, "Ada"); var b = Make(2, "Bo");
        a.Start();
        Check(Until(() => Drain(a, ea).Any(e => (string)e["e"] == "ready")), "Ada is in the room (a ticket at the localhost door, then the relay)");
        var err = ea.FirstOrDefault(e => (string)e["e"] == "error");
        if (err != null) { Console.WriteLine("  error: " + NetJson.Write(err)); return 1; }
        b.Start();
        Check(Until(() => Drain(b, eb).Any(e => (string)e["e"] == "ready")), "Bo is in");
        var readyB = eb.First(e => (string)e["e"] == "ready");
        Check(((string)readyB["data"]).Contains("Ada"), "Bo's ready lists Ada");
        Check(Until(() => Drain(a, ea).Any(e => (string)e["e"] == "join" && (string)e["name"] == "Bo")), "Ada hears Bo join");
        var aId = (string)ea.First(e => (string)e["e"] == "ready")["peer"];
        Check((string)readyB["host"] == aId, "the first in hosts");

        a.Send("chat", "{\"text\":\"hello\",\"n\":3}", true, "");
        a.SendText("say", "plain \"words\"", true, "");
        Check(Until(() => Drain(b, eb).Any(e => (string)e["e"] == "msg" && (string)e["kind"] == "say")), "Bo gets both messages");
        var chat = eb.First(e => (string)e["e"] == "msg" && (string)e["kind"] == "chat");
        Check(NetJson.Str(NetJson.Parse((string)chat["data"]), "text") == "hello" && (string)chat["peer"] == aId, "the JSON message arrives as JSON, from Ada");
        var say = eb.First(e => (string)e["e"] == "msg" && (string)e["kind"] == "say");
        Check((bool)say["text"] && (string)say["data"] == "plain \"words\"", "the text arrives as it was sent");

        a.Pose(1.5f, 2f, -3.25f, 1.25f, "{\"f\":\"balloon\"}");
        string poses = null;
        Check(Until(() => (poses = b.TakePoses()) != null), "Bo gets Ada's pose");
        var item = (Dictionary<string, object>)NetJson.Arr(NetJson.Parse(poses), "items")[0];
        Check((string)item["id"] == aId && (double)item["x"] == 1.5 && (double)item["z"] == -3.25 && (double)item["r"] == 1.25 && ((string)item["e"]).Contains("balloon"), "the pose: place, yaw and extras");
        Check(Math.Abs(a.Now() - b.Now()) < 1000, "one clock");

        a.SetAway(true);
        Check(Until(() => Drain(b, eb).Any(e => (string)e["e"] == "away" && (bool)e["away"])), "Bo hears Ada is away");
        Check(Until(() => Drain(b, eb).Any(e => (string)e["e"] == "host" && (string)e["reason"] == "host away")), "the host role moves off the away host");
        var c = Make(3, "Cy");
        c.Start();
        Check(Until(() => Drain(c, ec).Any(e => (string)e["e"] == "ready")), "Cy is in");
        var readyC = ec.First(e => (string)e["e"] == "ready");
        Check(((string)readyC["data"]).Contains("\"away\":true"), "a joiner hears who is away");

        b.Leave();
        Check(Until(() => Drain(a, ea).Any(e => (string)e["e"] == "leave" && (string)e["name"] == "Bo"), 30000), "Bo leaves; Ada hears it");
        a.Leave(); c.Leave();
        Console.WriteLine(failed == 0 ? "all managed checks passed" : failed + " check(s) failed");
        return failed == 0 ? 0 : 1;
    }
}
