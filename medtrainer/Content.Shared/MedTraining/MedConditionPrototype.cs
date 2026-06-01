using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.MedTraining;

/// <summary>
/// Condition randomizer
/// </summary>
[Prototype]
public sealed partial class MedConditionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name = string.Empty;

    [DataField("description")]
    public string Description = string.Empty;

    [DataField("isCritical")]
    public bool IsCritical;

    [DataField("damage", required: true)]
    public Dictionary<string, float> Damage = new();

    [DataField("treatmentHints")]
    public List<string> TreatmentHints = new();
}
