using KCxWare.Core.Loading;

namespace KCxWare.Tests;

public sealed class ChaosMessagesTests
{
    // The 100 user-supplied base sayings, verbatim, that must remain present in the CHAOS collection.
    private static readonly string[] RequiredBaseSayings =
    [
        "Windows found a problem. Shocking. Alert the historians.",
        "Your FPS just dropped harder than my expectations.",
        "Compilation failed. Apparently punctuation has feelings now.",
        "I fixed the bug. I have no idea how. Nobody touch anything.",
        "That code works, which frankly makes it more suspicious.",
        "Windows Update would like to ruin whatever plans you had.",
        "Task failed successfully. Peak computing.",
        "Your GPU is screaming. I'm calling that enthusiasm.",
        "RAM usage is approaching 'close Chrome or meet God' levels.",
        "The debugger has entered the chat. Everyone act innocent.",
        "Congratulations. You've discovered a bug nobody was looking for.",
        "I deleted the error. Technically that counts as debugging.",
        "Your computer needs a restart because apparently therapy wasn't an option.",
        "The code is held together by comments, caffeine, and unresolved trauma.",
        "Windows Defender inspected your code and requested hazard pay.",
        "That function has more problems than the family group chat.",
        "Loading… because instant disappointment would be too efficient.",
        "Your ping is so high the enemy killed you yesterday.",
        "Another exception. At least something around here has standards.",
        "I found the problem. It was between the keyboard and—never mind.",
        "The CPU is at 100%. Finally, someone around here is giving maximum effort.",
        "Your code compiled. Begin the ritual. We may never see this miracle again.",
        "Error 404: Motivation not found.",
        "Windows has detected that you're being productive and is taking corrective action.",
        "I've seen cleaner code scratched into bathroom stalls.",
        "Your frame time graph looks like a polygraph test.",
        "Rebooting: the IT equivalent of turning reality off and back on.",
        "The bug isn't reproducible. It knows we're watching.",
        "Your PC made a noise I'm pretty sure wasn't covered by the warranty.",
        "Don't worry. The smoke means the electrons are thinking harder.",
        "That variable name tells me future-you was never consulted.",
        "You wrote TODO six months ago. It's a historical landmark now.",
        "Windows would like administrator permission to ask for administrator permission.",
        "Your game crashed. Consider it an unscheduled rage-prevention feature.",
        "I optimized the code. It now fails significantly faster.",
        "The logs say everything is fine. The logs are filthy liars.",
        "Your GPU temperature is beginning negotiations with the sun.",
        "I found seventeen errors. Good news: sixteen are probably the first one wearing fake mustaches.",
        "The program works on your machine. Excellent. Ship your machine.",
        "Nothing says professional software like 'DO_NOT_DELETE_FINAL_FINAL2.'",
        "Autosave exists because software knows what kind of person you are.",
        "You missed the headshot. I'll blame input latency if you promise not to embarrass us again.",
        "The enemy didn't outplay you. Physics simply chose violence.",
        "That blue screen is Windows' way of rage quitting.",
        "I cleaned the cache. It was either that or burn the building down.",
        "Your dependencies have dependencies with abandonment issues.",
        "The build failed. Somewhere, a semicolon is laughing.",
        "Your code has entered the 'too dangerous to refactor' stage of adulthood.",
        "I would explain the error, but apparently the error doesn't understand itself either.",
        "We have successfully converted electricity into error messages.",
        "Your PC booted. Achievement unlocked: Bare Minimum.",
        "I checked the logs. We're going to need stronger coffee.",
        "The application is not responding. Neither am I emotionally.",
        "Your internet connection has chosen interpretive dance.",
        "Windows is configuring updates. Tell your family you loved them.",
        "The installer says two minutes remaining. That was twenty minutes ago. Time is a social construct.",
        "That memory leak has evolved into a memory flood.",
        "Your browser has 73 tabs open. At this point they're tenants.",
        "Chrome saw your unused RAM and took that personally.",
        "I killed the process. It knew what it did.",
        "The server responded with 500. Translation: 'I don't wanna.'",
        "The API changed without warning. Somewhere, a developer is smiling without knowing why.",
        "Your code passed every test. This is deeply concerning.",
        "Production is down. Quick, everyone stare at the person who deployed last.",
        "Git says there's a conflict. Violence was always an option.",
        "Merge conflict detected. Two developers entered. One codebase leaves.",
        "You force-pushed to main? Bold strategy, career-wise.",
        "Git remembers everything. Git is the creepy friend with receipts.",
        "That commit message says 'fix stuff.' Archaeologists will curse your bloodline.",
        "I reverted your changes. History has judged you.",
        "The database is locked. It has boundaries. Respect them.",
        "SQL returned zero rows. Even the database ghosted you.",
        "Your password is incorrect. Your confidence was adorable, though.",
        "Permission denied. The computer has finally learned to say no.",
        "Access violation. Somewhere, a pointer wandered into the wrong neighborhood.",
        "Null reference. You attempted to interact with literally nothing. Inspirational.",
        "Stack overflow. Your function thought recursion was a personality.",
        "Infinite loop detected. Congratulations, you invented software purgatory.",
        "Memory corruption detected. The computer has begun forgetting its childhood.",
        "The executable crashed so hard Windows briefly reconsidered electricity.",
        "Your controller disconnected exactly when you needed it. That feels personal.",
        "Packet loss detected. Your bullets are being shipped separately.",
        "Your teammate says 'trust me.' Statistically, this is where everything goes to hell.",
        "You're one update away from either +20 FPS or complete technological collapse.",
        "Graphics settings: Ultra. Decision-making settings: Low.",
        "Ray tracing enabled. Frames sold separately.",
        "Your GPU can render individual eyelashes but apparently not 144 stable FPS.",
        "CPU bottleneck detected. Your graphics card is waiting for Grandpa.",
        "Thermal throttling detected. Your PC has discovered self-preservation.",
        "Fan speed: 100%. Your case is now cleared for takeoff.",
        "I could optimize this, but watching brute force succeed is strangely beautiful.",
        "There are no bugs. Only undocumented acts of digital hostility.",
        "Somebody wrote this code at 3 A.M. You can smell the energy drink.",
        "That workaround has survived so long it legally qualifies as architecture.",
        "Never ask why it works. That's how you scare it.",
        "We touched one line and broke fourteen systems. Efficient.",
        "The computer did exactly what you told it to do. Unfortunately, you told it something stupid.",
        "Everything is operational. I'm uncomfortable with this level of peace.",
        "No errors detected. Either we're finished or the diagnostics are dead.",
        "KCxWare survived another session. Against the codebase's best efforts."
    ];

    [Fact]
    public void RequiredBaseSayings_AreAllPresentVerbatim()
    {
        Assert.Equal(100, RequiredBaseSayings.Length);
        foreach (var saying in RequiredBaseSayings)
        {
            Assert.Contains(saying, ChaosMessages.Sayings);
        }
    }

    [Fact]
    public void Collection_HasAtLeastOneHundredSayings()
    {
        Assert.True(ChaosMessages.Sayings.Count >= 100,
            $"Expected at least 100 CHAOS sayings, found {ChaosMessages.Sayings.Count}.");
    }

    [Fact]
    public void Collection_HasNoDuplicateSayings()
    {
        var distinct = ChaosMessages.Sayings.Distinct().Count();
        Assert.Equal(ChaosMessages.Sayings.Count, distinct);
    }

    [Fact]
    public void ShuffleBag_EmitsEveryItemExactlyOncePerCycle()
    {
        // A sequence long enough to drive several full cycles of the real CHAOS collection.
        var random = new SequencedRandomProvider(Enumerable.Range(0, 997).ToArray());
        var bag = ChaosMessages.CreateShuffleBag(random);

        for (var cycle = 0; cycle < 4; cycle++)
        {
            var emitted = new List<string>();
            for (var i = 0; i < ChaosMessages.Sayings.Count; i++)
            {
                emitted.Add(bag.Next());
            }

            // Every saying appears, none skipped, none duplicated within the cycle.
            Assert.Equal(ChaosMessages.Sayings.OrderBy(s => s), emitted.OrderBy(s => s));
        }
    }

    [Fact]
    public void ShuffleBag_SmallCollection_NeverRepeatsWithinACycle()
    {
        var items = new[] { "a", "b", "c", "d", "e" };
        var random = new SequencedRandomProvider(Enumerable.Range(0, 500).ToArray());
        var bag = new ShuffleBag<string>(items, random);

        for (var cycle = 0; cycle < 10; cycle++)
        {
            var emitted = new HashSet<string>();
            for (var i = 0; i < items.Length; i++)
            {
                Assert.True(emitted.Add(bag.Next()), "An item repeated before the cycle was exhausted.");
            }
        }
    }

    [Fact]
    public void ShuffleBag_AvoidsBoundaryRepeatWhenPossible()
    {
        // Deterministic fake: returns 0 every time, which (without the boundary-repeat guard) would
        // shuffle to the same first element on every cycle since index 0 always "wins" the swap draw
        // pattern used by Fisher-Yates with an all-zero random source.
        var items = new[] { "a", "b", "c" };
        var random = new SequencedRandomProvider(0);
        var bag = new ShuffleBag<string>(items, random);

        string? previousCycleLast = null;
        for (var cycle = 0; cycle < 5; cycle++)
        {
            var emitted = new List<string>();
            for (var i = 0; i < items.Length; i++)
            {
                emitted.Add(bag.Next());
            }

            if (previousCycleLast is not null)
            {
                Assert.NotEqual(previousCycleLast, emitted[0]);
            }

            previousCycleLast = emitted[^1];
        }
    }
}
