namespace SimulationPlatform.Application.Abstractions;

public enum ProposalCommandCheckpoint
{
    AfterDefinitionResolved,
    AfterDraftCreated,
    AfterProposalMutated,
    AfterAuditAdded,
    AfterOutboxAdded,
    BeforeIdempotencyCompleted
}

public interface IProposalCommandFaultInjector
{
    Task CheckpointAsync(ProposalCommandCheckpoint checkpoint, CancellationToken cancellationToken);
}

public sealed class NoOpProposalCommandFaultInjector : IProposalCommandFaultInjector
{
    public Task CheckpointAsync(ProposalCommandCheckpoint checkpoint, CancellationToken cancellationToken) => Task.CompletedTask;
}
