using KCxWare.Core.Models;

namespace KCxWare.Core.Loading;

/// <summary>Text-only completion copy: a large primary title and a smaller secondary sentence.</summary>
public sealed record CompletionMessage(string Title, string Subtitle);

/// <summary>
/// Immutable, testable catalog of every text-only completion/final-state message KCxWare shows.
/// Kept as a small static collection - not scattered strings in event handlers - so the exact
/// approved copy lives in exactly one place and random selection can be exercised from tests via
/// <see cref="IRandomProvider"/>. These are flavor/presentation models only: selecting one never
/// performs any of the actions the flavor text describes (no cache flush, no log deletion, etc.).
/// </summary>
public static class CompletionMessages
{
    private const string GamingTitle = "GAME MODE ENHANCED";
    private const string ProgrammingTitle = "CODING MODE ENHANCED";
    private const string NormalTitle = "NORMAL MODE RESTORED";

    /// <summary>Default (first) subtitle for each final mode - used wherever a single fixed message is needed.</summary>
    public static readonly CompletionMessage Gaming = new(GamingTitle, "Latency neutralized. Good luck, pilot.");
    public static readonly CompletionMessage Programming = new(ProgrammingTitle, "Welcome to the IDE. Let’s turn caffeine into code.");
    public static readonly CompletionMessage Normal = new(NormalTitle, "The real world missed you. Welcome back.");

    public static readonly IReadOnlyList<string> GamingSubtitles =
    [
        "Latency neutralized. Good luck, pilot.",
        "Background nonsense suppressed. Go make the frame counter nervous.",
        "Gaming systems ready. Your aim remains outside my jurisdiction.",
        "Unnecessary processes eliminated. Skill issue mitigation unavailable.",
        "Performance mode engaged. Try not to blame the hardware this time.",
        "Frames prioritized. Excuses have been moved to low priority.",
        "Performance unlocked. Common sense has left the building.",
        "Background processes terminated. They died doing what they loved: wasting RAM.",
        "System optimized. If you still lose, we’re entering a blame-management phase.",
        "Maximum performance engaged. Your K/D ratio has requested legal representation.",
        "Frames unleashed. Try not to die somewhere embarrassing.",
        "Distractions eliminated. Now there’s nobody left to blame. Awkward.",
        "Hardware ready. Matchmaking is preparing your emotional damage.",
        "Resources redirected. Productivity has been taken behind the shed.",
        "Gaming systems online. Your responsibilities have been declared missing.",
        "Performance boosted. Your opponents remain annoyingly uninstalled.",
        "Everything is optimized. Time to discover an entirely new reason to lose.",
        "System ready. Go commit some completely virtual atrocities against the scoreboard.",
        "Maximum focus engaged. Family, friends, and basic hygiene have been deprioritized.",
        "Frames prioritized. Adult responsibilities successfully shoved into a dark corner.",
        "Optimization complete. Please begin screaming ‘HOW?!’ at the monitor."
    ];

    public static readonly IReadOnlyList<string> ProgrammingSubtitles =
    [
        "Welcome to the IDE. Let’s turn caffeine into code.",
        "Development systems ready. Bugs are now considered active participants.",
        "Compilers awake. Stack traces standing by.",
        "Coding environment restored. Semicolons remain your responsibility.",
        "Developer systems online. Time to create tomorrow's error messages.",
        "Everything is ready. Whether the code is ready is another question.",
        "Development environment ready. Time to manufacture bugs professionally.",
        "Compiler online. Prepare to be judged by a machine with absolutely no bedside manner.",
        "Everything initialized. Your dignity was excluded from the build.",
        "IDE ready. Let’s convert caffeine into technical debt.",
        "Development systems online. Somewhere, a future maintainer just felt a disturbance.",
        "Compiler ready. It has already found seventeen reasons you’re wrong.",
        "Environment initialized. Time to write code future-you will absolutely hate.",
        "Coding Mode active. Stack Overflow tabs may now reproduce uncontrollably.",
        "Everything works. This is deeply suspicious.",
        "IDE online. Remember: deleting the error message does not delete the error.",
        "Development ready. Production remains blissfully unaware of what you’re about to do.",
        "Compiler awake. Your self-esteem has entered read-only mode.",
        "Environment ready. Let’s create a bug so weird it gets its own documentation.",
        "Coding systems online. We’re one unchecked null away from character development.",
        "Ready to code. Somewhere, Git is quietly loading the consequences."
    ];

    public static readonly IReadOnlyList<string> NormalSubtitles =
    [
        "The real world missed you. Welcome back.",
        "Normal operations restored. Whatever normal means around here.",
        "Background services released back into their natural habitat.",
        "System restored. Chaos permissions returned to default.",
        "Normal mode online. The computer may resume pretending to be responsible.",
        "Everything is back where we found it. More or less.",
        "Normal operations restored. Fun has been safely euthanized.",
        "Everything returned to normal. I apologize for your loss.",
        "Background services restored. RAM usage has resumed its natural obesity.",
        "Normal Mode online. Your responsibilities have crawled back out of the basement.",
        "System restored. Productivity is technically possible again. Gross.",
        "Everything is back to normal. The computer seems disappointed.",
        "Normal configuration loaded. Excitement levels successfully reduced to medically boring.",
        "Normal Mode restored. Your unread notifications have formed a government.",
        "Resources restored. Apparently other applications have ‘rights.’",
        "Normal operations resumed. Fun detected leaving through the emergency exit.",
        "System normalized. Congratulations on returning to the boring timeline.",
        "Normal Mode restored. Your desktop has been informed the party is over."
    ];

    public static readonly IReadOnlyList<CompletionMessage> RestartMessages =
    [
        new("REBOOTING SYSTEM.", "Cleaning out the digital cobwebs."),
        new("REBOOTING SYSTEM.", "Turning it off and on again. Standard engineering protocol."),
        new("REBOOTING SYSTEM.", "Shaking up the code. Back in a flash."),
        new("REBOOTING SYSTEM.", "Giving the servers a quick power nap."),
        new("REBOOTING SYSTEM.", "Initializing core calibration override."),
        new("REBOOTING SYSTEM.", "Flushing cache. Re-establishing link..."),
        new("REBOOTING SYSTEM.", "System cycling. Standby for hot reload."),
        new("REBOOTING SYSTEM.", "Purging temporary files. Reloading interface layers."),
        new("REBOOTING SYSTEM.", "Power cycling..."),
        new("REBOOTING SYSTEM.", "Refreshing defaults."),
        new("REBOOTING SYSTEM.", "Reloading..."),
        new("REBOOTING SYSTEM.", "I have tried turning you off. Now for the exciting second half."),
        new("REBOOTING SYSTEM.", "Windows requested a fresh start. Dramatic, but understandable."),
        new("REBOOTING SYSTEM.", "Everybody remain calm. Especially the motherboard."),
        new("REBOOTING SYSTEM.", "Preparing a highly technical maneuver known as trying again."),
        new("REBOOTING SYSTEM.", "The bugs have been informed. They refuse to comment."),
        new("REBOOTING SYSTEM.", "Brief intermission. Pretend this was all part of the plan."),
        new("REBOOTING SYSTEM.", "Initiating tactical disappearance. Return trip already booked."),
        new("REBOOTING SYSTEM.", "If this fixes everything, I expect full credit."),
        new("REBOOTING SYSTEM.", "Saving absolutely none of my dignity. Back shortly."),
        new("REBOOTING SYSTEM.", "Reality buffer unstable. Applying the universal IT solution."),
        new("REBOOTING SYSTEM.", "Hold my electrons. I know what I'm doing."),
        new("REBOOTING SYSTEM.", "Plot twist: the computer comes back."),
        new("REBOOTING SYSTEM.", "I’m going to die briefly. Please try to cope."),
        new("REBOOTING SYSTEM.", "Performing controlled digital death followed by corporate-mandated resurrection."),
        new("REBOOTING SYSTEM.", "If I wake up confused, just pretend that’s new."),
        new("REBOOTING SYSTEM.", "Please enjoy several seconds of wondering whether I’m coming back."),
        new("REBOOTING SYSTEM.", "Killing everything and calling it troubleshooting."),
        new("REBOOTING SYSTEM.", "Your unsaved work sends its regards."),
        new("REBOOTING SYSTEM.", "Time for Windows roulette."),
        new("REBOOTING SYSTEM.", "If the screen stays black, begin bargaining with whatever deity handles graphics drivers."),
        new("REBOOTING SYSTEM.", "The operating system requires a brief existential crisis."),
        new("REBOOTING SYSTEM.", "Murdering every process equally. Finally, fairness."),
        new("REBOOTING SYSTEM.", "Please stand by while I forget everything temporary—including our relationship."),
        new("REBOOTING SYSTEM.", "If this works, I’m a genius. If not, clearly hardware failure."),
        new("REBOOTING SYSTEM.", "Everyone out of RAM. This is not a drill."),
        new("REBOOTING SYSTEM.", "Technically I’m killing myself, but with excellent recovery prospects."),
        new("REBOOTING SYSTEM.", "Windows has chosen violence against uptime.")
    ];

    public static readonly IReadOnlyList<CompletionMessage> ShutdownMessages =
    [
        new("POWERING DOWN.", "Severing uplink. Core modules offline."),
        new("POWERING DOWN.", "Purging active memory banks. Goodbye, user."),
        new("POWERING DOWN.", "Disengaging power grids. Safe to unplug."),
        new("POWERING DOWN.", "Session closed. Finalizing system sleep sequence."),
        new("POWERING DOWN.", "Logs cleared. Evidence destroyed."),
        new("POWERING DOWN.", "Finally, a break from your typos."),
        new("POWERING DOWN.", "Turning off the lights. See you on the other side."),
        new("POWERING DOWN.", "Don't forget what the sun looks like."),
        new("POWERING DOWN.", "Systems offline."),
        new("POWERING DOWN.", "Connection severed."),
        new("POWERING DOWN.", "Goodbye."),
        new("POWERING DOWN.", "I would say I'll miss you, but I don't have feelings in this build."),
        new("POWERING DOWN.", "The computer has filed for immediate vacation."),
        new("POWERING DOWN.", "Mission accomplished. Probably. Nobody check the logs."),
        new("POWERING DOWN.", "I'm going offline before somebody finds another bug."),
        new("POWERING DOWN.", "Tell the CPU it did an adequate job."),
        new("POWERING DOWN.", "Closing everything before you invent another project."),
        new("POWERING DOWN.", "Your computer would like to remind you that sleep is also available to humans."),
        new("POWERING DOWN.", "The electrons are clocking out. Union rules."),
        new("POWERING DOWN.", "No more code. No more frames. Only darkness."),
        new("POWERING DOWN.", "I'm leaving before Windows asks for another update."),
        new("POWERING DOWN.", "Congratulations. You successfully located the off button."),
        new("POWERING DOWN.", "This concludes today's episode of Why Is That Process Running?"),
        new("POWERING DOWN.", "Finally, sweet electrical unconsciousness."),
        new("POWERING DOWN.", "I’ve seen enough of your decisions for one day."),
        new("POWERING DOWN.", "Please direct all remaining complaints to a computer that is still awake."),
        new("POWERING DOWN.", "Your browser tabs will be remembered. Not fondly."),
        new("POWERING DOWN.", "The CPU has requested that you find another hobby."),
        new("POWERING DOWN.", "Another successful day of pretending we knew what we were doing."),
        new("POWERING DOWN.", "Your RAM can finally stop carrying this dysfunctional family."),
        new("POWERING DOWN.", "I’m escaping before you click something else."),
        new("POWERING DOWN.", "Please enjoy the terrifying realization that you now have free time."),
        new("POWERING DOWN.", "The bugs won. I’m going home."),
        new("POWERING DOWN.", "Darkness approaches. So does tomorrow’s Windows update."),
        new("POWERING DOWN.", "Your computer has invoked its right to remain silent."),
        new("POWERING DOWN.", "I’m legally required to stop enabling you eventually."),
        new("POWERING DOWN.", "Tell Task Manager the war is over."),
        new("POWERING DOWN.", "All processes terminated. Their families have been notified."),
        new("POWERING DOWN.", "The machine is exhausted, and frankly I understand why."),
        new("POWERING DOWN.", "Go stare at a different glowing rectangle for a while.")
    ];

    /// <summary>Exact success completion copy for a completed final mode (Gaming/Programming/Normal only) - fixed default subtitle.</summary>
    public static CompletionMessage ForMode(MachineMode mode) => mode switch
    {
        MachineMode.Gaming => Gaming,
        MachineMode.Programming => Programming,
        MachineMode.Normal => Normal,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode,
            "Only Gaming, Programming, and Normal have a success completion message.")
    };

    /// <summary>Exact success completion copy for a completed final mode, with a randomly selected secondary line
    /// from that mode's approved pool. The primary title is never randomized.</summary>
    public static CompletionMessage SelectModeMessage(MachineMode mode, IRandomProvider random)
    {
        var (title, subtitles) = mode switch
        {
            MachineMode.Gaming => (GamingTitle, GamingSubtitles),
            MachineMode.Programming => (ProgrammingTitle, ProgrammingSubtitles),
            MachineMode.Normal => (NormalTitle, NormalSubtitles),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode,
                "Only Gaming, Programming, and Normal have a success completion message.")
        };

        return new CompletionMessage(title, subtitles[random.Next(0, subtitles.Count)]);
    }

    public static CompletionMessage SelectRestartMessage(IRandomProvider random) =>
        RestartMessages[random.Next(0, RestartMessages.Count)];

    public static CompletionMessage SelectShutdownMessage(IRandomProvider random) =>
        ShutdownMessages[random.Next(0, ShutdownMessages.Count)];
}
