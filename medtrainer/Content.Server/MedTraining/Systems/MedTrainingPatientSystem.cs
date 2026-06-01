using Content.Server.MedTraining.Components;
using Content.Server.NPC.HTN;
using Content.Server.Chat.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chat;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.MedTraining.Systems;

/// <summary>
/// Patient bvehavior after theyre healed
/// </summary>
public sealed class MedTrainingPatientSystem : EntitySystem
{
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float CheckInterval = 1.0f;
    private const float DespawnRange = 2.0f;
    private float _timer = 0f;

    public override void Update(float frameTime)
    {
        _timer += frameTime;
        if (_timer < CheckInterval)
            return;
        _timer = 0f;

        var query = EntityQueryEnumerator<MedTrainingPatientComponent, DamageableComponent, MobStateComponent>();

        while (query.MoveNext(out var uid, out var patient, out var damageable, out var mobState))
        {
            if (!patient.ThankedDoctor)
            {
                // Patients that dont reach reception within 15 seconds despawn
                if (!patient.IsCritical
                    && patient.ReceptionDeadline != TimeSpan.MaxValue
                    && _timing.CurTime > patient.ReceptionDeadline
                    && Exists(patient.ReceptionMarker))
                {
                    var patPos = _xform.GetMapCoordinates(uid);
                    var recMapPos = _xform.GetMapCoordinates(patient.ReceptionMarker);
                    if (patPos.MapId == recMapPos.MapId
                        && (patPos.Position - recMapPos.Position).Length() > 2.5f)
                    {
                        Log.Info($"MedTraining: patient {uid} failed to reach reception in time — despawning.");
                        QueueDel(uid);
                        continue;
                    }
                }

                CheckForHealing(uid, patient, damageable, mobState);
                continue;
            }

            // Check if close enough to spawn to despawn
            if (!patient.SpawnCoords.IsValid(EntityManager))
            {
                if (TryComp<HTNComponent>(uid, out var idleHtn2))
                {
                    idleHtn2.Blackboard.SetValue("MedPatientIdleTime", 99999f);
                    idleHtn2.RootTask = new HTNCompoundTask { Task = "MedPatientIdle" };
                    idleHtn2.Plan = null;
                }
                QueueDel(uid);
                continue;
            }

            var myPos = _xform.GetMapCoordinates(uid);
            var targetPos = _xform.ToMapCoordinates(patient.SpawnCoords);

            if (myPos.MapId != targetPos.MapId)
            {
                if (TryComp<HTNComponent>(uid, out var idleHtn3))
                {
                    idleHtn3.Blackboard.SetValue("MedPatientIdleTime", 99999f);
                    idleHtn3.RootTask = new HTNCompoundTask { Task = "MedPatientIdle" };
                    idleHtn3.Plan = null;
                }
                QueueDel(uid);
                continue;
            }

            if ((myPos.Position - targetPos.Position).Length() <= DespawnRange)
            {
                // Switch to idle
                if (TryComp<HTNComponent>(uid, out var idleHtn))
                {
                    idleHtn.Blackboard.SetValue("MedPatientIdleTime", 99999f);
                    idleHtn.RootTask = new HTNCompoundTask { Task = "MedPatientIdle" };
                    idleHtn.Plan = null;
                }
                QueueDel(uid);
            }
        }
    }

    private void CheckForHealing(EntityUid uid, MedTrainingPatientComponent patient,
        DamageableComponent damageable, MobStateComponent mobState)
    {
        // confirm patient is alive
        if (!_mobState.IsAlive(uid, mobState) || _mobState.IsCritical(uid, mobState))
            return;

        // Must be full health
        var totalDamage = _damageable.GetTotalDamage((uid, damageable));
        if (totalDamage > 0)
            return;

        // mark patient as healed and confirm if doctor is thnaked
        patient.ThankedDoctor = true;
        patient.IsTreated = true;

        // Unbuckle patient if theyre in bed or stasis bed
        if (TryComp<BuckleComponent>(uid, out var buckleComp) && buckleComp.Buckled)
        {
            _buckle.TryUnbuckle(uid, uid, true);
        }

        var bedQuery = EntityQueryEnumerator<MedTrainingStasisBedComponent>();
        while (bedQuery.MoveNext(out var bedUid, out var bed))
        {
            if (bed.CurrentPatient == uid)
            {
                bed.IsOccupied = false;
                bed.CurrentPatient = EntityUid.Invalid;
                break;
            }
        }

        _chat.TrySendInGameICMessage(uid, "Thanks doc!", InGameICChatType.Speak, false);
        StartWalkingHome(uid, patient);

        Log.Info($"MedTraining: patient {uid} healed and thanked doctor.");
    }

    private void StartWalkingHome(EntityUid uid, MedTrainingPatientComponent patient)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
        {
            QueueDel(uid);
            return;
        }

        htn.Blackboard.SetValue("MedPatientTargetCoords", patient.SpawnCoords);
        htn.Blackboard.SetValue("MedPatientRange", DespawnRange);
        htn.Blackboard.SetValue("MedPatientWaitMin", 0f);
        htn.Blackboard.SetValue("MedPatientWaitMax", 0f);
        htn.RootTask = new HTNCompoundTask { Task = "MedPatientWalkIn" };
        htn.Plan = null;
        _htn.Replan(htn);
    }
}
