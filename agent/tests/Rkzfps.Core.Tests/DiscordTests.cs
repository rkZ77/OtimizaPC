using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Engine;
using Rkzfps.Core.Model;
using Rkzfps.Core.Optimizations;
using Xunit;

namespace Rkzfps.Core.Tests;

public class DiscordTests
{
    private const long Gb = 1024L * 1024 * 1024;

    // Notebook com vídeo integrado e processador de 8 threads: o caso em que vale.
    private static SystemSnapshot WeakGpuPc(DiscordInfo? discord) => Pc.Healthy() with
    {
        Cpu = Pc.Healthy().Cpu! with { Threads = 8 },
        Gpus = [new GpuInfo { Name = "Intel UHD Graphics", Vendor = GpuVendor.Intel, LikelyIntegrated = true }],
        Discord = discord,
    };

    private static Evaluation Eval(SystemSnapshot s) => new DiscordHardwareAccelerationOptimization().Evaluate(new EvaluationContext(s, []));

    [Fact]
    public void Weak_gpu_with_cpu_room_recommends_turning_acceleration_off()
    {
        var e = Eval(WeakGpuPc(new DiscordInfo { HardwareAcceleration = true }));

        Assert.Equal(Decision.Recommended, e.Decision);
        var change = Assert.IsType<AppSettingChange>(Assert.Single(Assert.Single(e.Proposals).Changes));
        Assert.Equal(("discord", "enableHardwareAcceleration", "false"), (change.AppId, change.Key, change.Value));
    }

    [Fact]
    public void Strong_gpu_is_not_touched()
    {
        // Pc.Healthy tem RTX 3060 de 12 GB: Discord na GPU não pesa.
        var e = Eval(Pc.Healthy() with { Discord = new DiscordInfo { HardwareAcceleration = true } });
        Assert.Equal(Decision.NotApplicable, e.Decision);
        Assert.Empty(e.Proposals);
    }

    [Fact]
    public void Weak_gpu_and_weak_cpu_is_not_touched()
    {
        var s = WeakGpuPc(new DiscordInfo { HardwareAcceleration = true }) with { Cpu = Pc.Healthy().Cpu! with { Threads = 4 } };
        Assert.Equal(Decision.NotApplicable, Eval(s).Decision);
    }

    [Fact]
    public void Small_dedicated_card_counts_as_weak_gpu()
    {
        var s = WeakGpuPc(new DiscordInfo()) with { Gpus = [new GpuInfo { Name = "GTX 1050", Vendor = GpuVendor.Nvidia, VramBytes = 2 * Gb }] };
        Assert.Equal(Decision.Recommended, Eval(s).Decision);
    }

    [Fact]
    public void Already_off_is_optimal()
    {
        Assert.Equal(Decision.AlreadyOptimal, Eval(WeakGpuPc(new DiscordInfo { HardwareAcceleration = false })).Decision);
    }

    [Fact]
    public void Without_discord_nothing_is_offered_or_reported()
    {
        var s = WeakGpuPc(null);
        Assert.Equal(Decision.NotApplicable, Eval(s).Decision);
        Assert.Empty(new DiscordDiagnostic().Run(new EvaluationContext(s, [])));
    }

    [Fact]
    public void Diagnostic_points_to_the_fix_only_when_it_helps()
    {
        var weak = new DiscordDiagnostic().Run(new EvaluationContext(WeakGpuPc(new DiscordInfo()), [])).Single();
        Assert.Equal("discord-hardware-acceleration-off", weak.FixOptimizationId);
        Assert.Equal(HealthStatus.Attention, weak.Status);

        var strong = new DiscordDiagnostic().Run(new EvaluationContext(Pc.Healthy() with { Discord = new DiscordInfo() }, [])).Single();
        Assert.Null(strong.FixOptimizationId);
        Assert.Contains("overlay", strong.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Executor_refuses_while_discord_is_open_because_it_would_undo_the_change()
    {
        var sys = new FakeSystem { AppRunning = true };
        sys.AppSettings["enableHardwareAcceleration"] = "true";

        var ex = Assert.Throws<InvalidOperationException>(() => new ChangeExecutor(sys).Apply(new AppSettingChange("discord", "enableHardwareAcceleration", "false")));
        Assert.Contains("Feche o Discord", ex.Message);
        Assert.Equal("true", sys.AppSettings["enableHardwareAcceleration"]);
    }

    [Fact]
    public void Executor_applies_verifies_and_captures_inverse()
    {
        var sys = new FakeSystem();
        sys.AppSettings["enableHardwareAcceleration"] = "true";
        var exec = new ChangeExecutor(sys);
        var change = new AppSettingChange("discord", "enableHardwareAcceleration", "false");

        var inverse = Assert.IsType<AppSettingChange>(exec.CaptureInverse(change));
        exec.Apply(change);

        Assert.Equal("true", inverse.Value);
        Assert.True(exec.Verify(change).Passed);
        Assert.True(exec.StillApplied(change));
        exec.Apply(inverse);
        Assert.Equal("true", sys.AppSettings["enableHardwareAcceleration"]);
    }

    [Theory]
    [InlineData("discord", "enableHardwareAcceleration", "0")]
    [InlineData("discord", "MINIMIZE_TO_TRAY", "false")]
    [InlineData("discord", "chromiumSwitches", "{}")]
    [InlineData("steam", "enableHardwareAcceleration", "false")]
    public void Safety_policy_only_allows_the_whitelisted_key(string app, string key, string value)
    {
        Assert.Throws<SafetyViolationException>(() => SafetyPolicy.Validate(new AppSettingChange(app, key, value)));
    }
}
