using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Every curriculum level: valid spawn, and exit / checkpoint / ∂ tokens reachable with the real physics.</summary>
public class LevelReachabilityTests
{
    static string ReportPath => Path.Combine(Application.dataPath, "..", "Logs", "level-reachability-report.txt");

    [Test, Timeout(900000)]
    public void AllLevels_SpawnValid_AndGoalsReachable()
    {
        LevelAudit.AuditAll(out string summary, out int failing);
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, summary);
        TestContext.WriteLine(summary);
        Debug.Log(summary);
        Assert.AreEqual(0, failing, summary);
    }
}
