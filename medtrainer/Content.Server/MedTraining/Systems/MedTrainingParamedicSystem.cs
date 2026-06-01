using Content.Server.MedTraining.Components;
using Content.Server.NPC.HTN;
using Content.Server.Hands.Systems;
using Content.Shared.Buckle;
using Content.Shared.Hands.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.MedTraining.Systems;

public sealed class MedTrainingParamedicSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;

    private const float ArrivalRange = 2.5f;
    private const float CheckInterval = 0.1f;
    private const int MoveTicks = 1;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedTrainingParamedicComponent, ComponentStartup>(OnStartup);
    }
    private void OnStartup(EntityUid uid, MedTrainingParamedicComponent comp, ComponentStartup args)
    {
        comp.State = ParamedicState.WalkingToPatient;
        comp.TicksInState = 0;
        comp.NextActionTime = _timing.CurTime + TimeSpan.FromSeconds(0.5);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MedTrainingParamedicComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (now < comp.NextActionTime)
                continue;
            comp.NextActionTime = now + TimeSpan.FromSeconds(CheckInterval);
            comp.TicksInState++;

            switch (comp.State)
            {
                case ParamedicState.WalkingToPatient:
                    TickWalkToPatient(uid, comp, xform);
                    break;
                case ParamedicState.CarryingToStasis:
                    TickCarryToStasis(uid, comp, xform);
                    break;
                case ParamedicState.WalkingToExit:
                    TickWalkToExit(uid, comp, xform);
                    break;
                case ParamedicState.Done:
                    if (Exists(comp.RollerBed))
                        QueueDel(comp.RollerBed);
                    QueueDel(uid);
                    break;
            }
        }
    }

    private void TickWalkToPatient(EntityUid uid, MedTrainingParamedicComponent comp, TransformComponent xform)
    {
        if (!Exists(comp.Patient) || !Exists(comp.RollerBed))
        {
            comp.State = ParamedicState.Done;
            return;
        }

        var myPos = _xform.GetMapCoordinates(uid, xform);
        var patientPos = _xform.GetMapCoordinates(comp.Patient);
        if (myPos.MapId != patientPos.MapId) return;

        var dist = (patientPos.Position - myPos.Position).Length();

        if (!TryComp<HTNComponent>(uid, out var htn)) return;
        if (htn.Plan == null)
            WalkToward(uid, Transform(comp.Patient).Coordinates);

        if (comp.TicksInState < MoveTicks) return;

        if (dist > ArrivalRange) return;

        var buckled = _buckle.TryBuckle(comp.Patient, uid, comp.RollerBed);
        var pulled  = _pulling.TryStartPull(uid, comp.RollerBed);
        Log.Info($"MedTraining paramedic {uid}: at patient dist={dist:F2} Buckle={buckled} Pull={pulled}");

        Transition(uid, comp, ParamedicState.CarryingToStasis);
        WalkToward(uid, comp.StasisBedCoords, 1.5f);
    }
    private void TickCarryToStasis(EntityUid uid, MedTrainingParamedicComponent comp, TransformComponent xform)
    {
        if (!Exists(comp.TargetStasisBed))
        {
            Transition(uid, comp, ParamedicState.WalkingToExit);
            WalkToward(uid, comp.ExitCoords);
            return;
        }

        var myPos     = _xform.GetMapCoordinates(uid, xform);
        var bedCoords = Transform(comp.TargetStasisBed).Coordinates;
        var bedMapPos = _xform.GetMapCoordinates(comp.TargetStasisBed);

        if (myPos.MapId != bedMapPos.MapId) return;

        var dist = (bedMapPos.Position - myPos.Position).Length();

        if (dist > 1.8f)
        {
            if (!TryComp<HTNComponent>(uid, out var htn)) return;
            if (htn.Plan == null)
                WalkToward(uid, bedCoords, 1.5f);
            comp.ArrivalPhase = 0;
            return;
        }

        comp.ArrivalPhase++;

        if (comp.ArrivalPhase == 1)
        {
            if (Exists(comp.RollerBed) && TryComp<PullableComponent>(comp.RollerBed, out var pullComp))
                _pulling.TryStopPull(comp.RollerBed, pullComp);

            Log.Info($"paramedic {uid}: phase1");
            return; 
        }

        if (comp.ArrivalPhase == 2)
        {
            if (Exists(comp.TargetStasisBed))
            {
                var stasisPos = Transform(comp.TargetStasisBed).Coordinates;
                if (Exists(comp.RollerBed))
                    _xform.SetCoordinates(comp.RollerBed, stasisPos);
                if (Exists(comp.Patient))
                    _xform.SetCoordinates(comp.Patient, stasisPos);
            }

            if (Exists(comp.Patient))
                _buckle.TryUnbuckle(comp.Patient, uid, true);

            return; 
        }

        if (comp.ArrivalPhase >= 3)
        {
            bool buckled = false;
            if (Exists(comp.Patient) && Exists(comp.TargetStasisBed))
            {
                buckled = _buckle.TryBuckle(comp.Patient, uid, comp.TargetStasisBed);
                Log.Info($"MedTraining paramedic {uid}: phase3 buckle={buckled}");

                if (!buckled)
                    return;
            }

            if (TryComp<MedTrainingStasisBedComponent>(comp.TargetStasisBed, out var bedComp))
            {
                bedComp.IsOccupied = true;
                bedComp.CurrentPatient = comp.Patient;
            }

            bool pulled = false;
            if (Exists(comp.RollerBed))
                pulled = _pulling.TryStartPull(uid, comp.RollerBed);

            Log.Info($"MedTraining paramedic {uid}: done, heading to exit.");
            Transition(uid, comp, ParamedicState.WalkingToExit);
            WalkToward(uid, comp.ExitCoords);
        }
    }

    private void TickWalkToExit(EntityUid uid, MedTrainingParamedicComponent comp, TransformComponent xform)
    {
        var myPos     = _xform.GetMapCoordinates(uid, xform);
        var targetPos = _xform.ToMapCoordinates(comp.ExitCoords);

        if (myPos.MapId != targetPos.MapId) { comp.State = ParamedicState.Done; return; }

        if ((targetPos.Position - myPos.Position).Length() <= ArrivalRange * 2f)
        {
            comp.State = ParamedicState.Done;
            return;
        }

        if (!TryComp<HTNComponent>(uid, out var htn)) return;
        if (htn.Plan == null)
            WalkToward(uid, comp.ExitCoords);
    }

    private void Transition(EntityUid uid, MedTrainingParamedicComponent comp, ParamedicState next)
    {
        Log.Info($"MedTraining paramedic {uid}: {comp.State} -> {next}");
        comp.State = next;
        comp.TicksInState = 0;
        comp.ArrivalPhase = 0;
    }

    private void WalkToward(EntityUid uid, EntityCoordinates target, float? range = null)
    {
        if (!TryComp<HTNComponent>(uid, out var htn)) return;
        htn.Blackboard.SetValue("ParamedicMoveTarget", target);
        htn.Blackboard.SetValue("ParamedicArrivalRange", range ?? ArrivalRange);
        htn.RootTask = new HTNCompoundTask { Task = "MedParamedicMove" };
        htn.Plan = null;
        _htn.Replan(htn);
    }
}
