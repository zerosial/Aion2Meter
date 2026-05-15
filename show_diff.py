import json, sys
sys.stdout.reconfigure(encoding='utf-8')

with open('extracted_game_db.json', 'rb') as f:
    raw = f.read()
if raw[:3] == b'\xef\xbb\xbf': raw = raw[3:]
new_db = json.loads(raw.decode('utf-8-sig'))

with open('src/A2Meter/Data/game_db.json', 'r', encoding='utf-8-sig') as f:
    old_db = json.loads(f.read())

new_mobs = new_db.get('mobs', {})
old_mobs = old_db.get('mobs', {})
added = {k: v for k, v in new_mobs.items() if k not in old_mobs}

print("=== New Boss Monsters (12) ===")
for k, v in sorted(added.items()):
    print(f"  Code {k}: {v['name']} (Lv.{v['level']}, isBoss={v['isBoss']})")

new_dungeons = new_db.get('dungeons', {})
old_dungeons = old_db.get('dungeons', {})
added_d = {k: v for k, v in new_dungeons.items() if k not in old_dungeons}
print(f"\n=== New Dungeons (4) ===")
for k, v in sorted(added_d.items()):
    print(f"  Code {k}: {v['name']}")
