using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.MedTraining.Components;

[RegisterComponent]
public sealed partial class MedTrainingScenarioComponent : Component
{
    [DataField("lobbySpawnPoint")]
    public EntityUid LobbySpawnPoint;

    [DataField("paramedicSpawnPoint")]
    public EntityUid ParamedicSpawnPoint;

    [DataField("receptionMarker")]
    public EntityUid ReceptionMarker;

    [DataField("stasisBeds")]
    public List<EntityUid> StasisBeds = new();
}

[RegisterComponent]
public sealed partial class MedTrainingButtonComponent : Component
{
    [DataField("lastActivated")]
    public TimeSpan LastActivated = TimeSpan.MinValue;
}

[RegisterComponent]
public sealed partial class MedTrainingPatientComponent : Component
{
    [DataField("condition")]
    public string Condition = string.Empty;

    [DataField("isCritical")]
    public bool IsCritical;

    [DataField("isTreated")]
    public bool IsTreated;

    [DataField("spawnCoords")]
    public EntityCoordinates SpawnCoords;

    public bool ThankedDoctor = false;

    /// <summary>
    /// checks if patient has reached the necessary entity. despawn if not
    /// </summary>
    public TimeSpan ReceptionDeadline = TimeSpan.MaxValue;

    public EntityUid ReceptionMarker = EntityUid.Invalid;
}

public enum ParamedicState
{
    WalkingToPatient,
    CarryingToStasis,
    WalkingToExit,
    Done
}

[RegisterComponent]
public sealed partial class MedTrainingParamedicComponent : Component
{
    [DataField("patient")]
    public EntityUid Patient;

    [DataField("rollerBed")]
    public EntityUid RollerBed;

    [DataField("targetStasisBed")]
    public EntityUid TargetStasisBed = EntityUid.Invalid;

    [DataField("stasisBedCoords")]
    public EntityCoordinates StasisBedCoords;

    [DataField("exitCoords")]
    public EntityCoordinates ExitCoords;

    public ParamedicState State = ParamedicState.WalkingToPatient;
    public TimeSpan NextActionTime = TimeSpan.Zero;
    public int TicksInState = 0;
    // unbuckle tick 1 rebuckle tick 2
    public int ArrivalPhase = 0;

}

[RegisterComponent]
public sealed partial class MedTrainingStasisBedComponent : Component
{
    [DataField("isOccupied")]
    public bool IsOccupied;

    [DataField("currentPatient")]
    public EntityUid CurrentPatient = EntityUid.Invalid;
}

[RegisterComponent]
public sealed partial class MedTrainingLobbySpawnComponent : Component { }

[RegisterComponent]
public sealed partial class MedTrainingParamedicSpawnComponent : Component { }

[RegisterComponent]
public sealed partial class MedTrainingReceptionMarkerComponent : Component { }
