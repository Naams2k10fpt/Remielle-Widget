using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Remielle.Core.Tests;

[TestClass]
public sealed class WidgetStateMachineTests
{
    [TestMethod]
    public async Task RequiredConversationTransitionsWork()
    {
        using var machine = CreateMachine();

        machine.Handle(new(WidgetEventKind.ComposerActivated));
        await WaitForState(machine, WidgetState.UserTyping);

        machine.Handle(new(WidgetEventKind.PromptSubmitted));
        Assert.AreEqual(WidgetState.AiThinking, machine.CurrentState);

        machine.Handle(new(WidgetEventKind.AiOutputChanged));
        await WaitForState(machine, WidgetState.AiTyping);

        machine.Handle(new(WidgetEventKind.AiCompleted));
        Assert.AreEqual(WidgetState.AiComplete, machine.CurrentState);
    }

    [TestMethod]
    public async Task ComposerDebounceUsesLatestEvent()
    {
        using var machine = CreateMachine();
        var changes = 0;
        machine.StateChanged += _ => changes++;

        machine.Handle(new(WidgetEventKind.ComposerActivated));
        await Task.Delay(5);
        machine.Handle(new(WidgetEventKind.ComposerCleared));
        await Task.Delay(60);

        Assert.AreEqual(WidgetState.Waiting, machine.CurrentState);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public async Task AiCompleteResetsToWaiting()
    {
        using var machine = CreateMachine();
        await MoveToAiTyping(machine);

        machine.Handle(new(WidgetEventKind.AiCompleted));
        await WaitForState(machine, WidgetState.Waiting);
    }

    [TestMethod]
    public async Task NewComposerActivityCancelsOldCompletionTimer()
    {
        using var machine = CreateMachine();
        await MoveToAiTyping(machine);
        machine.Handle(new(WidgetEventKind.AiCompleted));

        await Task.Delay(20);
        machine.Handle(new(WidgetEventKind.ComposerActivated));
        await WaitForState(machine, WidgetState.UserTyping);
        await Task.Delay(100);

        Assert.AreEqual(WidgetState.UserTyping, machine.CurrentState);
    }

    [TestMethod]
    public async Task CooldownDeliversLatestValidTransition()
    {
        using var machine = CreateMachine();
        machine.Handle(new(WidgetEventKind.PromptSubmitted));
        machine.Handle(new(WidgetEventKind.AiOutputChanged));

        await WaitForState(machine, WidgetState.AiTyping);
        machine.Handle(new(WidgetEventKind.AiPaused));

        await WaitForState(machine, WidgetState.AiThinking);
    }

    [TestMethod]
    public async Task ImmediateCompletionCancelsPendingOutputTransition()
    {
        using var machine = CreateMachine(
            cooldown: TimeSpan.FromMilliseconds(80),
            completeDuration: TimeSpan.FromMilliseconds(300));
        machine.Handle(new(WidgetEventKind.PromptSubmitted));
        machine.Handle(new(WidgetEventKind.AiOutputChanged));
        machine.Handle(new(WidgetEventKind.AiCompleted));

        await Task.Delay(120);

        Assert.AreEqual(WidgetState.AiComplete, machine.CurrentState);
    }

    [TestMethod]
    public async Task DisconnectReturnsToWaitingWithoutDuplicateNotification()
    {
        using var machine = CreateMachine();
        var changes = new List<WidgetState>();
        machine.StateChanged += changes.Add;

        machine.Handle(new(WidgetEventKind.ComposerActivated));
        await WaitForState(machine, WidgetState.UserTyping);
        machine.Handle(new(WidgetEventKind.ObserverDisconnected));
        machine.Handle(new(WidgetEventKind.ObserverDisconnected));

        Assert.AreEqual(WidgetState.Waiting, machine.CurrentState);
        Assert.AreEqual(1, changes.Count(state => state == WidgetState.Waiting));
    }

    private static WidgetStateMachine CreateMachine(
        TimeSpan? cooldown = null,
        TimeSpan? completeDuration = null) =>
        new(
            composerDebounce: TimeSpan.FromMilliseconds(30),
            cooldown: cooldown ?? TimeSpan.FromMilliseconds(20),
            completeDuration: completeDuration ?? TimeSpan.FromMilliseconds(80));

    private static async Task MoveToAiTyping(WidgetStateMachine machine)
    {
        machine.Handle(new(WidgetEventKind.PromptSubmitted));
        machine.Handle(new(WidgetEventKind.AiOutputChanged));
        await WaitForState(machine, WidgetState.AiTyping);
    }

    private static async Task WaitForState(
        WidgetStateMachine machine,
        WidgetState expected,
        int timeoutMilliseconds = 1_000)
    {
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        while (machine.CurrentState != expected)
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
