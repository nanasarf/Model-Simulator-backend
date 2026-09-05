using Microsoft.EntityFrameworkCore;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<OrganizationRow> Organizations => Set<OrganizationRow>();
    public DbSet<CourseRow> Courses => Set<CourseRow>();
    public DbSet<ClassroomRow> Classrooms => Set<ClassroomRow>();
    public DbSet<EnrollmentRow> Enrollments => Set<EnrollmentRow>();
    public DbSet<SimulationDefinitionRow> SimulationDefinitions => Set<SimulationDefinitionRow>();
    public DbSet<ScenarioVersionRow> ScenarioVersions => Set<ScenarioVersionRow>();
    public DbSet<ScenarioDraftRow> ScenarioDrafts => Set<ScenarioDraftRow>();
    public DbSet<AssessmentCommentRow> AssessmentComments => Set<AssessmentCommentRow>();
    public DbSet<RuleDefinitionRow> Rules => Set<RuleDefinitionRow>();
    public DbSet<SessionRow> Sessions => Set<SessionRow>();
    public DbSet<SessionManifestRow> SessionManifests => Set<SessionManifestRow>();
    public DbSet<TeamRow> Teams => Set<TeamRow>();
    public DbSet<ParticipantRow> Participants => Set<ParticipantRow>();
    public DbSet<RoundReadinessRow> RoundReadiness => Set<RoundReadinessRow>();
    public DbSet<RoleAssignmentRow> RoleAssignments => Set<RoleAssignmentRow>();
    public DbSet<ActionSubmissionRow> ActionSubmissions => Set<ActionSubmissionRow>();
    public DbSet<SnapshotRow> Snapshots => Set<SnapshotRow>();
    public DbSet<EventRow> Events => Set<EventRow>();
    public DbSet<IdempotencyRow> IdempotencyRecords => Set<IdempotencyRow>();
    public DbSet<ExecutionRow> Executions => Set<ExecutionRow>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<AuditRow> AuditRecords => Set<AuditRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<OrganizationRow>(e => { e.ToTable("organizations", "education"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200); });
        b.Entity<CourseRow>(e => { e.ToTable("courses", "education"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique(); e.HasOne<OrganizationRow>().WithMany().HasForeignKey(x => x.OrganizationId); });
        b.Entity<ClassroomRow>(e => { e.ToTable("classrooms", "education"); e.HasKey(x => x.Id); e.HasOne<CourseRow>().WithMany().HasForeignKey(x => x.CourseId); });
        b.Entity<EnrollmentRow>(e => { e.ToTable("enrollments", "education"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ClassroomId, x.UserId }).IsUnique(); e.HasOne<ClassroomRow>().WithMany().HasForeignKey(x => x.ClassroomId); });
        b.Entity<SimulationDefinitionRow>(e => { e.ToTable("simulation_definitions", "definitions"); e.HasKey(x => x.Id); });
        b.Entity<ScenarioVersionRow>(e => { e.ToTable("scenario_versions", "definitions"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.SimulationDefinitionId, x.Version }).IsUnique(); e.Property(x => x.ConfigurationJson).HasColumnType("jsonb"); e.Property(x => x.ManifestJson).HasColumnType("jsonb"); e.Property(x => x.ManifestHash).HasMaxLength(32); });
        b.Entity<ScenarioDraftRow>(e => { e.ToTable("scenario_drafts", "definitions"); e.HasKey(x => x.Id); e.Property(x => x.ContentJson).HasColumnType("jsonb"); e.Property(x => x.Version).IsConcurrencyToken(); e.HasIndex(x => new { x.SimulationDefinitionId, x.Name }); e.HasOne<SimulationDefinitionRow>().WithMany().HasForeignKey(x => x.SimulationDefinitionId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<AssessmentCommentRow>(e => { e.ToTable("assessment_comments", "learning"); e.HasKey(x => x.Id); e.Property(x => x.Version).IsConcurrencyToken(); e.HasIndex(x => new { x.SessionId, x.TargetType, x.TargetId }); });
        b.Entity<RuleDefinitionRow>(e => { e.ToTable("rules", "definitions"); e.HasKey(x => x.Id); e.Property(x => x.ConditionJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.ScenarioVersionId, x.Priority }); e.HasOne<ScenarioVersionRow>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<SessionRow>(e => { e.ToTable("sessions", "runtime"); e.HasKey(x => x.Id); e.Property(x => x.Version).IsConcurrencyToken(); e.HasOne<ClassroomRow>().WithMany().HasForeignKey(x => x.ClassroomId); e.HasOne<ScenarioVersionRow>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<SessionManifestRow>(e => { e.ToTable("session_manifests", "runtime"); e.HasKey(x => x.SessionId); e.Property(x => x.ManifestJson).HasColumnType("jsonb"); e.Property(x => x.ManifestHash).HasMaxLength(32); e.HasOne<SessionRow>().WithOne().HasForeignKey<SessionManifestRow>(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<TeamRow>(e => { e.ToTable("teams", "runtime"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.SessionId, x.Name }).IsUnique(); e.HasOne<SessionRow>().WithMany().HasForeignKey(x => x.SessionId); });
        b.Entity<ParticipantRow>(e => { e.ToTable("participants", "runtime"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.SessionId, x.UserId }).IsUnique(); e.HasOne<SessionRow>().WithMany().HasForeignKey(x => x.SessionId); e.HasOne<TeamRow>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<RoundReadinessRow>(e => { e.ToTable("round_readiness", "runtime"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.SessionId, x.RoundNumber, x.Phase, x.UserId }).IsUnique(); e.HasOne<SessionRow>().WithMany().HasForeignKey(x => x.SessionId); });
        b.Entity<RoleAssignmentRow>(e => { e.ToTable("role_assignments", "runtime"); e.HasKey(x => x.Id); e.Property(x => x.CapabilitiesJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.SessionId, x.TeamId, x.UserId, x.RoleCode }).IsUnique(); e.HasOne<SessionRow>().WithMany().HasForeignKey(x => x.SessionId); e.HasOne<TeamRow>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<ActionSubmissionRow>(e => { e.ToTable("action_submissions", "runtime"); e.HasKey(x => x.Id); e.Property(x => x.PayloadJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique(); });
        b.Entity<SnapshotRow>(e => { e.ToTable("snapshots", "runtime"); e.HasKey(x => x.Id); e.Property(x => x.StateJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.SessionId, x.TeamId, x.RoundNumber }).IsUnique(); });
        b.Entity<EventRow>(e => { e.ToTable("events", "runtime"); e.HasKey(x => x.Id); e.Property(x => x.Sequence).UseIdentityByDefaultColumn(); e.Property(x => x.DataJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique(); });
        b.Entity<IdempotencyRow>(e => { e.ToTable("idempotency_records", "runtime"); e.HasKey(x => x.Id); e.Property(x => x.RequestHash).HasMaxLength(32); e.HasIndex(x => new { x.UserId, x.Operation, x.Key }).IsUnique(); });
        b.Entity<ExecutionRow>(e => { e.ToTable("executions", "runtime"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.SessionId, x.TeamId, x.RoundNumber }).IsUnique(); });
        b.Entity<OutboxMessage>(e => { e.ToTable("outbox_messages", "integration"); e.HasKey(x => x.Id); e.Property(x => x.PayloadJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt }); });
        b.Entity<AuditRow>(e => { e.ToTable("audit_records", "audit"); e.HasKey(x => x.Id); e.Property(x => x.MetadataJson).HasColumnType("jsonb"); });
    }
}

public sealed class OrganizationRow { public Guid Id { get; set; } public required string Name { get; set; } public Guid OwnerUserId { get; set; } }
public sealed class CourseRow { public Guid Id { get; set; } public Guid OrganizationId { get; set; } public Guid OwnerUserId { get; set; } public required string Code { get; set; } public required string Name { get; set; } }
public sealed class ClassroomRow { public Guid Id { get; set; } public Guid CourseId { get; set; } public required string Name { get; set; } }
public sealed class EnrollmentRow { public Guid Id { get; set; } public Guid ClassroomId { get; set; } public Guid UserId { get; set; } public DateTimeOffset EnrolledAt { get; set; } }
public sealed class SimulationDefinitionRow { public Guid Id { get; set; } public Guid OwnerUserId { get; set; } public required string Name { get; set; } }
public sealed class ScenarioVersionRow { public Guid Id { get; set; } public Guid SimulationDefinitionId { get; set; } public int Version { get; set; } public required string Name { get; set; } public required string ModelIdentifier { get; set; } public required string ModelVersion { get; set; } public required string ConfigurationJson { get; set; } public required string ManifestJson { get; set; } public required byte[] ManifestHash { get; set; } public DateTimeOffset PublishedAt { get; set; } }
public sealed class ScenarioDraftRow { public Guid Id { get; set; } public Guid SimulationDefinitionId { get; set; } public Guid OwnerUserId { get; set; } public required string Name { get; set; } public required string Status { get; set; } public required string ContentJson { get; set; } public long Version { get; set; } public Guid? PublishedScenarioVersionId { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } }
public sealed class AssessmentCommentRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public required string TargetType { get; set; } public Guid TargetId { get; set; } public Guid AuthorUserId { get; set; } public required string Text { get; set; } public long Version { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } }
public sealed class RuleDefinitionRow { public Guid Id { get; set; } public Guid ScenarioVersionId { get; set; } public int Priority { get; set; } public required string Effect { get; set; } public required string ConditionJson { get; set; } public bool Enabled { get; set; } }
public sealed class SessionRow { public Guid Id { get; set; } public Guid ClassroomId { get; set; } public Guid ScenarioVersionId { get; set; } public required string ModelIdentifier { get; set; } public required string ModelVersion { get; set; } public required string Status { get; set; } public required string Phase { get; set; } public int RoundNumber { get; set; } public int Seed { get; set; } public long Version { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } }
public sealed class SessionManifestRow { public Guid SessionId { get; set; } public required string ManifestJson { get; set; } public required byte[] ManifestHash { get; set; } public DateTimeOffset FrozenAt { get; set; } }
public sealed class TeamRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public required string Name { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class ParticipantRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public Guid UserId { get; set; } public Guid? TeamId { get; set; } public bool IsReady { get; set; } public DateTimeOffset JoinedAt { get; set; } public DateTimeOffset? ReadyAt { get; set; } }
public sealed class RoundReadinessRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public Guid UserId { get; set; } public int RoundNumber { get; set; } public required string Phase { get; set; } public bool IsReady { get; set; } public DateTimeOffset ChangedAt { get; set; } }
public sealed class RoleAssignmentRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public Guid TeamId { get; set; } public Guid UserId { get; set; } public Guid RoleDefinitionId { get; set; } public required string RoleCode { get; set; } public required string CapabilitiesJson { get; set; } public DateTimeOffset AssignedAt { get; set; } public DateTimeOffset? RevokedAt { get; set; } }
public sealed class ActionSubmissionRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public int RoundNumber { get; set; } public Guid TeamId { get; set; } public Guid UserId { get; set; } public Guid RoleAssignmentId { get; set; } public required string ActionCode { get; set; } public required string PayloadJson { get; set; } public required string IdempotencyKey { get; set; } public DateTimeOffset SubmittedAt { get; set; } public required string SubmittedPhase { get; set; } }
public sealed class SnapshotRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public Guid TeamId { get; set; } public int RoundNumber { get; set; } public required string ModelIdentifier { get; set; } public required string ModelVersion { get; set; } public required string StateJson { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class EventRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public long Sequence { get; set; } public int RoundNumber { get; set; } public Guid? ActorId { get; set; } public required string Type { get; set; } public required string DataJson { get; set; } public DateTimeOffset OccurredAt { get; set; } }
public sealed class IdempotencyRow { public Guid Id { get; set; } public Guid UserId { get; set; } public required string Operation { get; set; } public required string Key { get; set; } public required byte[] RequestHash { get; set; } public int ResponseStatus { get; set; } public Guid? ResultId { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset ExpiresAt { get; set; } }
public sealed class ExecutionRow { public Guid Id { get; set; } public Guid SessionId { get; set; } public Guid TeamId { get; set; } public int RoundNumber { get; set; } public required string Status { get; set; } public DateTimeOffset StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } }
public sealed class OutboxMessage { public Guid Id { get; set; } public required string Type { get; set; } public Guid AggregateId { get; set; } public required string PayloadJson { get; set; } public DateTimeOffset OccurredAt { get; set; } public int Attempts { get; set; } public DateTimeOffset NextAttemptAt { get; set; } public Guid? ClaimId { get; set; } public DateTimeOffset? ClaimedUntil { get; set; } public DateTimeOffset? ProcessedAt { get; set; } public string? LastError { get; set; } }
public sealed class AuditRow { public Guid Id { get; set; } public Guid? ActorUserId { get; set; } public required string Action { get; set; } public required string ResourceType { get; set; } public required string ResourceId { get; set; } public required string TraceId { get; set; } public required string MetadataJson { get; set; } public DateTimeOffset OccurredAt { get; set; } }
