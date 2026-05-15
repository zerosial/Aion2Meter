"""
A2Meter.exe 전체 디컴파일 도구
================================
.NET 8 단일 파일(Single-File) 번들에서 모든 DLL/리소스를 추출하고,
A2Meter.dll을 C# 소스코드로 디컴파일합니다.

사용법:
    python decompile_full.py A2Meter.exe [출력폴더]
"""

import sys
import os
import struct
import zlib
import subprocess
import shutil
import json

# 콘솔 인코딩 설정
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def find_manifest_entries(data):
    """
    번들 매니페스트 끝(파일 끝 근처)에서 모든 엔트리를 파싱합니다.
    
    엔트리 형식:
      offset(int64) | size(int64) | compressed_size(int64) | type(byte) | name_len(7bit_int) | name(utf-8)
    """
    entries = []
    file_len = len(data)
    
    # game_db.json의 위치로 매니페스트 영역을 특정
    anchor = data.find(b'Data/game_db.json')
    if anchor < 0:
        print("ERROR: Data/game_db.json not found")
        return []
    
    # 매니페스트는 파일 끝 ~300KB 범위에 있음
    # anchor에서 역방향으로 매니페스트 시작점을 찾음
    manifest_end = min(anchor + 5000, file_len)
    
    # 매니페스트의 각 엔트리를 순차적으로 파싱하기 위해
    # 시작점을 찾아야 함. 역방향 탐색은 어려우므로,
    # 모든 알려진 파일 확장자를 검색하여 엔트리를 역추적
    
    # 매니페스트 영역 범위 (보수적으로 넓게)
    search_start = max(0, anchor - 500000)
    search_end = manifest_end
    
    # 파일명 끝 패턴으로 모든 엔트리를 찾음
    extensions = [
        b'.dll', b'.json', b'.png', b'.html', b'.js',
        b'.manifest', b'.so', b'.dylib', b'.pdb',
    ]
    
    candidates = []
    for ext in extensions:
        idx = search_start
        while True:
            idx = data.find(ext, idx, search_end)
            if idx < 0:
                break
            # 파일명 끝 위치
            name_end = idx + len(ext)
            # 역방향으로 ASCII printable 범위를 추적하여 이름 시작 찾기
            name_start = name_end - 1
            while name_start > name_end - 300 and name_start > 0:
                ch = data[name_start - 1]
                # ASCII printable 범위 (공백~틸드, 슬래시, 점 등)
                if 32 <= ch <= 126:
                    name_start -= 1
                else:
                    break
            
            name_bytes = data[name_start:name_end]
            name_len = len(name_bytes)
            
            if name_len < 3 or name_len > 200:
                idx += 1
                continue
            
            # name_len 바이트 검증
            if name_len < 128:
                if data[name_start - 1] != name_len:
                    idx += 1
                    continue
                type_pos = name_start - 2
            else:
                # 7-bit encoding (2 bytes)
                b0 = data[name_start - 2]
                b1 = data[name_start - 1]
                decoded_len = (b0 & 0x7F) | ((b1 & 0x7F) << 7)
                if decoded_len != name_len:
                    idx += 1
                    continue
                type_pos = name_start - 3
            
            type_byte = data[type_pos]
            if type_byte > 10:
                idx += 1
                continue
            
            try:
                comp_size = struct.unpack_from('<q', data, type_pos - 8)[0]
                size = struct.unpack_from('<q', data, type_pos - 16)[0]
                offset = struct.unpack_from('<q', data, type_pos - 24)[0]
            except:
                idx += 1
                continue
            
            # 유효성 검사
            if not (0 <= offset < file_len and 0 < size < file_len and 0 <= comp_size < file_len):
                idx += 1
                continue
            
            name = name_bytes.decode('ascii', errors='replace')
            candidates.append({
                'name': name,
                'offset': offset,
                'size': size,
                'compressed_size': comp_size,
                'type': type_byte,
                'manifest_pos': type_pos - 24,
            })
            
            idx += 1
    
    # 중복 제거 (같은 이름이면 매니페스트 영역에 더 가까운 것을 선택)
    by_name = {}
    for c in candidates:
        name = c['name']
        if name not in by_name or c['manifest_pos'] > by_name[name]['manifest_pos']:
            by_name[name] = c
    
    entries = sorted(by_name.values(), key=lambda e: e['offset'])
    return entries


def extract_entry(data, entry, output_dir):
    """번들 엔트리 하나를 추출합니다."""
    name = entry['name']
    offset = entry['offset']
    size = entry['size']
    comp_size = entry['compressed_size']
    
    out_path = os.path.join(output_dir, name.replace('/', os.sep))
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    
    if comp_size > 0 and comp_size != size:
        compressed = data[offset:offset + comp_size]
        for wbits in [-15, 15, 31]:
            try:
                decompressed = zlib.decompress(compressed, wbits)
                with open(out_path, 'wb') as f:
                    f.write(decompressed)
                return True, len(decompressed)
            except:
                continue
        # 실패 시 원본 저장
        with open(out_path, 'wb') as f:
            f.write(compressed)
        return False, comp_size
    else:
        raw = data[offset:offset + size]
        with open(out_path, 'wb') as f:
            f.write(raw)
        return True, size


def decompile_with_ilspy(dll_path, output_dir):
    """ilspycmd로 DLL을 C# 프로젝트로 디컴파일합니다."""
    try:
        result = subprocess.run(
            ['ilspycmd', '-p', '-o', output_dir, dll_path],
            capture_output=True, text=True, timeout=120
        )
        return result.returncode == 0
    except FileNotFoundError:
        print("  ERROR: ilspycmd not installed. Run: dotnet tool install -g ilspycmd")
        return False
    except subprocess.TimeoutExpired:
        print("  WARNING: ILSpy timeout (120s)")
        return False


def main():
    if len(sys.argv) < 2:
        print("Usage: python decompile_full.py <A2Meter.exe> [output_dir]")
        sys.exit(1)
    
    exe_path = sys.argv[1]
    output_dir = sys.argv[2] if len(sys.argv) > 2 else 'scratch_decompile/decompiled_latest'
    extracted_dir = os.path.join(output_dir, '_extracted')
    
    print(f"=== A2Meter.exe Full Decompile Tool ===")
    print(f"Input: {exe_path}")
    print(f"Output: {output_dir}")
    print()
    
    data = open(exe_path, 'rb').read()
    print(f"File size: {len(data):,} bytes\n")
    
    # Step 1: Parse manifest
    print("=== Step 1: Parse bundle manifest ===")
    entries = find_manifest_entries(data)
    if not entries:
        print("FAILED: No bundle entries found.")
        sys.exit(1)
    
    print(f"Found {len(entries)} entries\n")
    
    # Step 2: Extract all files
    print(f"=== Step 2: Extract files -> {extracted_dir} ===")
    os.makedirs(extracted_dir, exist_ok=True)
    
    ok_count = 0
    fail_count = 0
    for entry in entries:
        ok, size = extract_entry(data, entry, extracted_dir)
        tag = "OK" if ok else "FAIL"
        comp = ""
        if entry['compressed_size'] > 0 and entry['compressed_size'] != entry['size']:
            comp = f" (compressed: {entry['compressed_size']:,})"
        print(f"  [{tag}] {entry['name']} - {size:,} bytes{comp}")
        if ok:
            ok_count += 1
        else:
            fail_count += 1
    
    print(f"\nExtracted: {ok_count} OK, {fail_count} FAIL")
    
    # Step 3: Decompile A2Meter.dll with ILSpy
    a2_dll = os.path.join(extracted_dir, 'A2Meter.dll')
    ilspy_dir = os.path.join(output_dir, '_ilspy_output')
    
    if os.path.exists(a2_dll):
        print(f"\n=== Step 3: Decompile A2Meter.dll with ILSpy ===")
        if os.path.exists(ilspy_dir):
            shutil.rmtree(ilspy_dir)
        
        if decompile_with_ilspy(a2_dll, ilspy_dir):
            cs_count = 0
            for root, dirs, files in os.walk(ilspy_dir):
                for f in files:
                    if f.endswith('.cs'):
                        cs_count += 1
                        rel = os.path.relpath(os.path.join(root, f), ilspy_dir)
                        print(f"    {rel}")
            print(f"  Total: {cs_count} .cs files decompiled")
        else:
            print("  Decompile failed")
    
    # Step 4: Report
    print(f"\n=== Step 4: Summary Report ===")
    report = {
        'source': exe_path,
        'file_size': len(data),
        'total_entries': len(entries),
        'entries': [{k: v for k, v in e.items() if k != 'manifest_pos'} for e in entries],
    }
    report_path = os.path.join(output_dir, '_extraction_report.json')
    with open(report_path, 'w', encoding='utf-8') as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    print(f"  Report: {report_path}")
    
    print(f"\n=== DONE ===")
    print(f"  Extracted: {extracted_dir}")
    print(f"  Decompiled: {ilspy_dir}")


if __name__ == '__main__':
    main()
