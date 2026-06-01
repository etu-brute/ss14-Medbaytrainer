using Content.Server.MedTraining.Components;
using Content.Shared.Buckle.Components;
using Robust.Shared.GameObjects;

namespace Content.Server.MedTraining.Systems;


public sealed class MedTrainingStasisBedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedTrainingStasisBedComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<MedTrainingStasisBedComponent, UnstrappedEvent>(OnUnstrapped);
    }

    // args.Buckle is Entity "BuckleComponent
    private void OnStrapped(EntityUid uid, MedTrainingStasisBedComponent bed, ref StrappedEvent args)
    {
        bed.IsOccupied     = true;
        bed.CurrentPatient = args.Buckle.Owner;
    }

    private void OnUnstrapped(EntityUid uid, MedTrainingStasisBedComponent bed, ref UnstrappedEvent args)
    {
        bed.IsOccupied     = false;
        bed.CurrentPatient = EntityUid.Invalid;
    }
}
