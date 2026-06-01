using System.Linq;
using Content.Server.MedTraining.Components;
using Content.Server.NPC.HTN;

using Content.Shared.MedTraining;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;

using Content.Shared.Interaction;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;
using Content.Shared.Chat;
using Content.Shared.Inventory;
using Content.Server.Chat.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Map;

namespace Content.Server.MedTraining.Systems;

public sealed class MedTrainingSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly HTNSystem _htn = default!;

    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly HumanoidProfileSystem _humanoidProfile = default!;

    private bool _patientActive = false;

    private const string KeyPatientTargetCoords = "MedPatientTargetCoords";
    private const string KeyPatientRange        = "MedPatientRange";
    private const string KeyPatientWaitMin      = "MedPatientWaitMin";
    private const string KeyPatientWaitMax      = "MedPatientWaitMax";
    private const string KeyPatientCoords       = "PatientCoords";
    private const string KeyPatientEntity       = "PatientEntity";
    private const string KeyRollerBedEntity     = "RollerBedEntity";
    private const string KeyReceptionCoords     = "ReceptionCoords";
    private const string KeyStasisBedCoords     = "StasisBedCoords";
    private const string KeyStasisBedEntity     = "StasisBedEntity";
    private const string KeyParamedicRange      = "ParamedicArrivalRange";
    private const string KeyExitCoords          = "ExitCoords";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedTrainingButtonComponent, ActivateInWorldEvent>(OnButtonActivated);
        SubscribeLocalEvent<MedTrainingButtonComponent, InteractHandEvent>(OnButtonInteractHand);
        SubscribeLocalEvent<MedTrainingButtonComponent, InteractUsingEvent>(OnButtonInteractUsing);
        SubscribeLocalEvent<MedTrainingPatientComponent, EntityTerminatingEvent>(OnPatientDespawned);
    }

    private void OnButtonInteractHand(EntityUid uid, MedTrainingButtonComponent button, InteractHandEvent args)
    {
        if (args.Handled) return;
        TryActivateButton(uid);
        args.Handled = true;
    }

    private void OnButtonInteractUsing(EntityUid uid, MedTrainingButtonComponent button, InteractUsingEvent args)
    {
        if (args.Handled) return;
        TryActivateButton(uid);
        args.Handled = true;
    }

    private void TryActivateButton(EntityUid uid)
    {
        if (_patientActive)
            return;
        _patientActive = true;
        var xform = Transform(uid);
        if (!TryFindScenario(xform.GridUid, out var scenario, out var scenarioComp))
        {
            Log.Warning("MedTraining: button pressed but no scenario coordinator found!");
            return;
        }
        RefreshScenarioLinks(xform.GridUid!.Value, scenarioComp!);
        SpawnPatient(scenarioComp!, scenario!.Value);
    }

    private void OnPatientDespawned(EntityUid uid, MedTrainingPatientComponent comp, ref EntityTerminatingEvent args)
    {
        // if patients are despawned, timer resets
        var remaining = 0;
        var query = EntityQueryEnumerator<MedTrainingPatientComponent>();
        while (query.MoveNext(out var otherUid, out _))
        {
            if (otherUid != uid)
                remaining++;
        }

        if (remaining == 0)
        {
            _patientActive = false;
            Log.Info("MedTraining: all patients gone — button re-enabled.");
        }
    }

    private void OnButtonActivated(EntityUid uid, MedTrainingButtonComponent button, ActivateInWorldEvent args)
    {
        if (args.Handled) return;
        TryActivateButton(uid);
        args.Handled = true;
    }

    private void RefreshScenarioLinks(EntityUid gridUid, MedTrainingScenarioComponent scenario)
    {
        var lobbyQuery = EntityQueryEnumerator<MedTrainingLobbySpawnComponent, TransformComponent>();
        while (lobbyQuery.MoveNext(out var ent, out _, out var xform))
        {
            if (xform.GridUid == gridUid) { scenario.LobbySpawnPoint = ent; break; }
        }

        var paramQuery = EntityQueryEnumerator<MedTrainingParamedicSpawnComponent, TransformComponent>();
        while (paramQuery.MoveNext(out var ent, out _, out var xform))
        {
            if (xform.GridUid == gridUid) { scenario.ParamedicSpawnPoint = ent; break; }
        }

        var receptionQuery = EntityQueryEnumerator<MedTrainingReceptionMarkerComponent, TransformComponent>();
        while (receptionQuery.MoveNext(out var ent, out _, out var xform))
        {
            if (xform.GridUid == gridUid) { scenario.ReceptionMarker = ent; break; }
        }

        scenario.StasisBeds.Clear();
        var bedQuery = EntityQueryEnumerator<MedTrainingStasisBedComponent, TransformComponent>();
        while (bedQuery.MoveNext(out var ent, out _, out var xform))
        {
            if (xform.GridUid == gridUid) scenario.StasisBeds.Add(ent);
        }

        Log.Info($"MedTraining: links refreshed - Lobby={scenario.LobbySpawnPoint.IsValid()}, Paramedic={scenario.ParamedicSpawnPoint.IsValid()}, Reception={scenario.ReceptionMarker.IsValid()}, Beds={scenario.StasisBeds.Count}");
    }

    private void SpawnPatient(MedTrainingScenarioComponent scenario, EntityUid scenarioEnt)
    {
        if (_random.Prob(0.01f))
        {
            SpawnNarsie(scenario);
            return;
        }

        var isCrit = _random.Prob(0.4f);
        var condition = PickCondition(isCrit);
        Log.Info($"MedTraining: spawning {(isCrit ? "critical" : "ambulatory")} patient with condition {condition.ID}");
        if (isCrit)
            SpawnCriticalPatient(scenario, condition);
        else
            SpawnAmbulatoryPatient(scenario, condition);
    }

    private void SpawnNarsie(MedTrainingScenarioComponent scenario)
    {
        if (!TryGetSpawnCoords(scenario.LobbySpawnPoint, out var spawnCoords)) return;
        Log.Info("MedTraining: Nar'Sie has risen!");
        Spawn("MobNarsieSpawn", spawnCoords);
    }

    private void SpawnAmbulatoryPatient(MedTrainingScenarioComponent scenario, MedConditionPrototype condition)
    {
        if (!TryGetSpawnCoords(scenario.LobbySpawnPoint, out var spawnCoords)) return;
        if (!TryGetSpawnCoords(scenario.ReceptionMarker, out var receptionCoords)) return;
        var patient = Spawn(GetRandomPatientProto(), spawnCoords);
        ApplyConditionDamage(patient, condition);

        var tag = EnsureComp<MedTrainingPatientComponent>(patient);
        tag.Condition   = condition.ID;
        tag.IsCritical  = false;
        tag.SpawnCoords = spawnCoords;

        if (!TryComp<HTNComponent>(patient, out var htn)) return;

        // walk to lobby entity, wait til healed. 
        htn.Blackboard.SetValue(KeyPatientTargetCoords, receptionCoords);
        htn.Blackboard.SetValue(KeyPatientRange, 1.5f);
        htn.Blackboard.SetValue(KeyPatientWaitMin, 99999f);
        htn.Blackboard.SetValue(KeyPatientWaitMax, 99999f);
        htn.Blackboard.SetValue("MedPatientIdleTime", 99999f);
        htn.RootTask = new HTNCompoundTask { Task = "MedPatientWalkIn" };
        htn.Plan = null;
        _htn.Replan(htn);

        Log.Info($"MedTraining: ambulatory patient {patient} spawned, walking to reception lobby.");
    }



    private void SpawnCriticalPatient(MedTrainingScenarioComponent scenario, MedConditionPrototype condition)
    {
        if (!TryGetSpawnCoords(scenario.ParamedicSpawnPoint, out var paramedicSpawnCoords)) return;
        if (!TryGetSpawnCoords(scenario.LobbySpawnPoint, out var lobbyCoords)) return;
        var patient = Spawn(GetRandomPatientProto(), lobbyCoords);
        ApplyConditionDamage(patient, condition);

        var tag = EnsureComp<MedTrainingPatientComponent>(patient);
        tag.Condition   = condition.ID;
        tag.IsCritical  = true;
        tag.SpawnCoords = lobbyCoords;

        var rollerBed = Spawn("EmergencyRollerBed", lobbyCoords);
        var paramedic = Spawn("MobMedTrainingParamedic", paramedicSpawnCoords);
        EquipParamedic(paramedic);

        _chat.TrySendInGameICMessage(paramedic, "Incoming critical patient! All medics to the lobby!", InGameICChatType.Speak, false);

        var bedUid = FindFreeStasisBed(scenario);
        TryGetSpawnCoords(bedUid, out var bedCoords);

        // produce paramedic tand interact with medtrainer scenario system
        var paramComp = EnsureComp<MedTrainingParamedicComponent>(paramedic);
        paramComp.Patient         = patient;
        paramComp.RollerBed       = rollerBed;
        paramComp.TargetStasisBed = bedUid;
        paramComp.StasisBedCoords = bedCoords;
        paramComp.ExitCoords      = paramedicSpawnCoords;

        // paramedic walks to lobby
        if (!TryComp<HTNComponent>(paramedic, out var htn)) return;

        htn.Blackboard.SetValue("ParamedicMoveTarget", lobbyCoords);
        htn.Blackboard.SetValue(KeyParamedicRange, 1.8f);
        htn.RootTask = new HTNCompoundTask { Task = "MedParamedicMove" };
        htn.Plan = null;
        _htn.Replan(htn);

        Log.Info($"MedTraining: critical patient {patient} at lobby, paramedic {paramedic} dispatched. Stasis bed: {bedUid}");
    }

    private void EquipParamedic(EntityUid paramedic)
    {
        if (!TryComp<InventoryComponent>(paramedic, out var inv)) return;
        var invSystem = EntityManager.System<InventorySystem>();
        var coords = Transform(paramedic).Coordinates;

        // replace items in paramedic inventory with correct equipment and remove default spawn items like synd bag
        foreach (var slot in new[] { "jumpsuit", "outerClothing", "ears", "gloves", "back", "belt", "neck" })
        {
            if (invSystem.TryGetSlotEntity(paramedic, slot, out var existing))
            {
                invSystem.TryUnequip(paramedic, slot, true, true, inventory: inv);
                if (existing.HasValue && Exists(existing.Value))
                    QueueDel(existing.Value);
            }
        }

        // giuve paramedic loadout
        var jumpsuit = Spawn("ClothingUniformJumpsuitParamedic", coords);
        invSystem.TryEquip(paramedic, jumpsuit, "jumpsuit", true, inventory: inv);

        var outer = Spawn("ClothingOuterHardsuitVoidParamed", coords);
        invSystem.TryEquip(paramedic, outer, "outerClothing", true, inventory: inv);

        var headset = Spawn("ClothingHeadsetMedical", coords);
        invSystem.TryEquip(paramedic, headset, "ears", true, inventory: inv);

        var gloves = Spawn("ClothingHandsGlovesLatex", coords);
        invSystem.TryEquip(paramedic, gloves, "gloves", true, inventory: inv);
    }

    private MedConditionPrototype PickCondition(bool isCrit)
    {
        var all = _proto.EnumeratePrototypes<MedConditionPrototype>()
            .Where(p => p.IsCritical == isCrit)
            .ToList();
        return _random.Pick(all);
    }

    private void ApplyConditionDamage(EntityUid ent, MedConditionPrototype condition)
    {
        if (!TryComp<DamageableComponent>(ent, out _)) return;

        // 1-5 condition 5=crit
        int severity = condition.IsCritical ? 5 : _random.Next(1, 5);

        // Severity multipliers
        float multiplier = severity switch
        {
            1 => 0.25f,
            2 => 0.50f,
            3 => 0.75f,
            4 => 1.00f,
            _ => 1.00f,
        };

        foreach (var (type, baseAmount) in condition.Damage)
        {
            if (!_proto.TryIndex<DamageTypePrototype>(type, out var dmgType)) continue;

            float scaled = baseAmount * multiplier;
            float variance = scaled * 0.2f;
            float roll = _random.NextFloat(-variance, variance);
            float result = scaled + roll;

            // r2m randomization modifier for pt condition. Condition keeps being either too high or to minimal
            if (roll > 0 && _random.Prob(0.333f))
                result -= variance;
            else if (roll < 0 && _random.Prob(0.333f))
                result += variance;

            // condition caps so players dont arrive dead or gib immediately on spawn lol
            if (type == "Blunt")
                result = Math.Min(result, 180f);
            result = Math.Min(result, 185f);
            result = Math.Max(result, 1f);

            _damageable.TryChangeDamage(ent, new DamageSpecifier(dmgType, (float)Math.Round(result)), true);
        }
    }

    private void SetCritical(EntityUid ent)
    {
        if (!TryComp<DamageableComponent>(ent, out _)) return;
        var blunt = _proto.Index(new ProtoId<DamageTypePrototype>("Blunt"));
        _damageable.TryChangeDamage(ent, new DamageSpecifier(blunt, 150), true);
    }

    private EntityUid FindFreeStasisBed(MedTrainingScenarioComponent scenario)
    {
        foreach (var bed in scenario.StasisBeds)
        {
            if (Exists(bed) && TryComp<MedTrainingStasisBedComponent>(bed, out var c) && !c.IsOccupied)
                return bed;
        }
        return EntityUid.Invalid;
    }

    private string GetRandomPatientProto()
    {
        var protos = new[]
        {
            "MobMedTrainingPatientHuman",
            "MobMedTrainingPatientReptilian",
            "MobMedTrainingPatientMoth",
            "MobMedTrainingPatientArachnid",
        };
        return protos[_random.Next(protos.Length)];
    }


    private bool TryGetSpawnCoords(EntityUid marker, out EntityCoordinates coords)
    {
        coords = default;
        if (!Exists(marker)) return false;
        coords = Transform(marker).Coordinates;
        return true;
    }

    private bool TryFindScenario(EntityUid? gridUid, out EntityUid? scenarioEnt, out MedTrainingScenarioComponent? comp)
    {
        scenarioEnt = null;
        comp = null;
        var query = EntityQueryEnumerator<MedTrainingScenarioComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scenario, out var xform))
        {
            if (xform.GridUid == gridUid) { scenarioEnt = uid; comp = scenario; return true; }
        }
        return false;
    }
}
