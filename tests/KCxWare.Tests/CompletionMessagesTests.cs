using KCxWare.Core.Loading;
using KCxWare.Core.Models;

namespace KCxWare.Tests;

public sealed class CompletionMessagesTests
{
    [Fact]
    public void Gaming_MapsToExactApprovedCopy()
    {
        var message = CompletionMessages.ForMode(MachineMode.Gaming);
        Assert.Equal("GAME MODE ENHANCED", message.Title);
        Assert.Equal("Latency neutralized. Good luck, pilot.", message.Subtitle);
    }

    [Fact]
    public void Programming_MapsToExactApprovedCopy()
    {
        var message = CompletionMessages.ForMode(MachineMode.Programming);
        Assert.Equal("CODING MODE ENHANCED", message.Title);
        Assert.Equal("Welcome to the IDE. Let’s turn caffeine into code.", message.Subtitle);
    }

    [Fact]
    public void Normal_MapsToExactApprovedCopy()
    {
        var message = CompletionMessages.ForMode(MachineMode.Normal);
        Assert.Equal("NORMAL MODE RESTORED", message.Title);
        Assert.Equal("The real world missed you. Welcome back.", message.Subtitle);
    }

    [Theory]
    [InlineData(MachineMode.GamingArmed)]
    [InlineData(MachineMode.ProgrammingArmed)]
    [InlineData(MachineMode.NormalArmed)]
    [InlineData(MachineMode.RecoveryRequired)]
    public void FailedOrNonFinalModes_HaveNoSuccessCompletionCopy(MachineMode mode)
    {
        // Only Gaming, Programming, Normal - the three successful final modes - have completion
        // copy; anything else (including a failed transition's RecoveryRequired) must not be
        // mapped to success text, so callers can never accidentally show it after a failure.
        Assert.Throws<ArgumentOutOfRangeException>(() => CompletionMessages.ForMode(mode));
    }

    [Fact]
    public void RestartRandomSelection_OnlySelectsFromApprovedRestartMessages()
    {
        for (var i = 0; i < CompletionMessages.RestartMessages.Count; i++)
        {
            var selected = CompletionMessages.SelectRestartMessage(new SequencedRandomProvider(i));
            Assert.Contains(selected, CompletionMessages.RestartMessages);
            Assert.StartsWith("REBOOTING SYSTEM.", selected.Title);
        }
    }

    [Fact]
    public void ShutdownRandomSelection_OnlySelectsFromApprovedShutdownMessages()
    {
        for (var i = 0; i < CompletionMessages.ShutdownMessages.Count; i++)
        {
            var selected = CompletionMessages.SelectShutdownMessage(new SequencedRandomProvider(i));
            Assert.Contains(selected, CompletionMessages.ShutdownMessages);
            Assert.StartsWith("POWERING DOWN.", selected.Title);
        }
    }

    [Fact]
    public void RestartAndShutdownMessages_AreDisjointApprovedSets()
    {
        Assert.Equal(38, CompletionMessages.RestartMessages.Count);
        Assert.Equal(40, CompletionMessages.ShutdownMessages.Count);
        Assert.Empty(CompletionMessages.RestartMessages.Select(m => m.Subtitle)
            .Intersect(CompletionMessages.ShutdownMessages.Select(m => m.Subtitle)));
    }

    [Theory]
    [InlineData(MachineMode.Gaming, "GAME MODE ENHANCED")]
    [InlineData(MachineMode.Programming, "CODING MODE ENHANCED")]
    [InlineData(MachineMode.Normal, "NORMAL MODE RESTORED")]
    public void SelectModeMessage_NeverRandomizesTheTitle(MachineMode mode, string expectedTitle)
    {
        for (var i = 0; i < 6; i++)
        {
            var selected = CompletionMessages.SelectModeMessage(mode, new SequencedRandomProvider(i));
            Assert.Equal(expectedTitle, selected.Title);
        }
    }

    [Fact]
    public void SelectModeMessage_OnlySelectsFromApprovedSubtitlePools()
    {
        for (var i = 0; i < CompletionMessages.GamingSubtitles.Count; i++)
        {
            var selected = CompletionMessages.SelectModeMessage(MachineMode.Gaming, new SequencedRandomProvider(i));
            Assert.Contains(selected.Subtitle, CompletionMessages.GamingSubtitles);
        }

        for (var i = 0; i < CompletionMessages.ProgrammingSubtitles.Count; i++)
        {
            var selected = CompletionMessages.SelectModeMessage(MachineMode.Programming, new SequencedRandomProvider(i));
            Assert.Contains(selected.Subtitle, CompletionMessages.ProgrammingSubtitles);
        }

        for (var i = 0; i < CompletionMessages.NormalSubtitles.Count; i++)
        {
            var selected = CompletionMessages.SelectModeMessage(MachineMode.Normal, new SequencedRandomProvider(i));
            Assert.Contains(selected.Subtitle, CompletionMessages.NormalSubtitles);
        }
    }

    [Theory]
    [InlineData(MachineMode.GamingArmed)]
    [InlineData(MachineMode.ProgrammingArmed)]
    [InlineData(MachineMode.NormalArmed)]
    [InlineData(MachineMode.RecoveryRequired)]
    public void SelectModeMessage_RejectsNonFinalModes(MachineMode mode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CompletionMessages.SelectModeMessage(mode, new SequencedRandomProvider(0)));
    }
}
