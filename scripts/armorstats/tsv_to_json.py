import csv
import json

input_file = "stats.tsv"
sign_file = "playerstat_signs.json"
output_file = "stats_converted.json"

# Load saved sign convention
try:
    with open(sign_file, "r", encoding="utf-8") as f:
        stat_signs = json.load(f)
except FileNotFoundError:
    stat_signs = {}

data = {}

def parse_percent(value):
    """Convert '5%' back to 0.05 float."""
    if not value:
        return 0
    v = value.replace("%", "").strip()
    try:
        return float(v) / 100
    except ValueError:
        return 0

with open(input_file, "r", encoding="utf-8") as f:
    reader = csv.DictReader(f, delimiter="\t")
    for row in reader:
        name = row["Name"]
        player_stats = {}
        for stat in ["walkspeed", "manipulationSpeed", "steadyAim", "healingeffectivness", "hungerrate"]:
            val = parse_percent(row[stat])
            sign = stat_signs.get(stat, 1)
            player_stats[stat] = val * sign

        data[name] = {
            "Layers": [x.strip() for x in row["Layers"].split(",")] if row["Layers"] else [],
            "Zones": [x.strip() for x in row["Zones"].split(",")] if row["Zones"] else [],
            "Resists": {
                "PiercingAttack": float(row["PiercingAttack"]) if row["PiercingAttack"] else 0,
                "SlashingAttack": float(row["SlashingAttack"]) if row["SlashingAttack"] else 0,
                "BluntAttack": float(row["BluntAttack"]) if row["BluntAttack"] else 0,
            },
            "FlatReduction": {},
            "PlayerStats": player_stats,
        }

with open(output_file, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)

print(f"Converted {input_file} -> {output_file}")
