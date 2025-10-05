import json
import csv

input_file = "stats.json"
output_file = "stats.tsv"

with open(input_file, "r", encoding="utf-8") as f:
    data = json.load(f)

# Determine sign convention for PlayerStats (from first nonzero value)
stat_signs = {}
for entry in data.values():
    player_stats = entry.get("PlayerStats", {})
    for k, v in player_stats.items():
        if v != 0 and k not in stat_signs:
            stat_signs[k] = -1 if v < 0 else 1

rows = []
for name, entry in data.items():
    parts = name.split("-")
    type_part = parts[1] if len(parts) > 1 else ""
    subtype_part = parts[2] if len(parts) > 2 else ""
    material_part = parts[-1] if len(parts) > 1 else ""

    layers = entry.get("Layers", [])
    zones = entry.get("Zones", [])
    resists = entry.get("Resists", {})
    player_stats = entry.get("PlayerStats", {})

    # Convert PlayerStats to positive integer percentages
    def fmt_stat(stat):
        v = player_stats.get(stat, 0)
        return f"{int(round(abs(v) * 100))}%" if v != 0 else "0%"

    row = {
        "Name": name,
        "Type": type_part,
        "Subtype": subtype_part,
        "Material": material_part,
        "Layers": ", ".join(layers),
        "LayerCount": len(layers),
        "Zones": ", ".join(zones),
        "ZoneCount": len(zones),
        "PiercingAttack": resists.get("PiercingAttack", ""),
        "SlashingAttack": resists.get("SlashingAttack", ""),
        "BluntAttack": resists.get("BluntAttack", ""),
        "walkspeed": fmt_stat("walkspeed"),
        "manipulationSpeed": fmt_stat("manipulationSpeed"),
        "steadyAim": fmt_stat("steadyAim"),
        "healingeffectivness": fmt_stat("healingeffectivness"),
        "hungerrate": fmt_stat("hungerrate"),
    }
    rows.append(row)

# Write TSV
with open(output_file, "w", newline="", encoding="utf-8") as f:
    writer = csv.DictWriter(f, fieldnames=rows[0].keys(), delimiter="\t")
    writer.writeheader()
    writer.writerows(rows)

# Save sign convention for reimport
with open("playerstat_signs.json", "w", encoding="utf-8") as f:
    json.dump(stat_signs, f, indent=2)

print(f"Converted {input_file} -> {output_file}")
print("Saved PlayerStat sign conventions to playerstat_signs.json")
