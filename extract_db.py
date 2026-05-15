import sys, struct, zlib

exe_path = sys.argv[1] if len(sys.argv) > 1 else 'A2Meter.exe'
data = open(exe_path, 'rb').read()

name_str = b'Data/game_db.json'
name_abs = data.find(name_str)
print(f"'Data/game_db.json' at: {name_abs}")

type_pos = name_abs - 2
compressed_size = struct.unpack_from('<q', data, type_pos - 8)[0]
size = struct.unpack_from('<q', data, type_pos - 16)[0]
offset = struct.unpack_from('<q', data, type_pos - 24)[0]
print(f"Offset={offset}, Size={size}, CompressedSize={compressed_size}")

compressed_data = data[offset:offset + compressed_size]
decompressed = zlib.decompress(compressed_data, -15)
print(f"Decompressed: {len(decompressed)} bytes")

with open('extracted_game_db.json', 'wb') as f:
    f.write(decompressed)
print("Saved to extracted_game_db.json")

# Also extract known_skills_catalog.json  
name2 = b'Data/known_skills_catalog.json'
name2_abs = data.find(name2)
if name2_abs >= 0:
    offset2 = struct.unpack_from('<q', data, name2_abs - 26)[0]
    size2 = struct.unpack_from('<q', data, name2_abs - 18)[0]
    comp2 = struct.unpack_from('<q', data, name2_abs - 10)[0]
    dec2 = zlib.decompress(data[offset2:offset2+comp2], -15)
    with open('extracted_known_skills.json', 'wb') as f:
        f.write(dec2)
    print(f"Saved extracted_known_skills.json ({len(dec2)} bytes)")
