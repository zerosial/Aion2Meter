// A2Updater — 본체(A2Meter.exe) 종료 대기 → 삭제 → 다운로드 → 재실행.
//
// Usage:
//   A2Updater.exe --target "C:\path\A2Meter.exe" --url "https://..." --pid 1234
//
// --target : 교체할 대상 exe 경로
// --url    : 새 버전 다운로드 URL
// --pid    : 종료 대기할 프로세스 ID (본체)

using System.Diagnostics;
using System.Net.Http;

namespace A2Updater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? target = null;
        string? url = null;
        int pid = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target": target = args[++i]; break;
                case "--url":    url = args[++i]; break;
                case "--pid":    pid = int.Parse(args[++i]); break;
            }
        }

        if (target == null || url == null)
        {
            Console.Error.WriteLine("Usage: A2Updater --target <exe> --url <download-url> [--pid <id>]");
            return 1;
        }

        try
        {
            Console.WriteLine("[A2Updater] 업데이트 시작");

            // 1. 본체 프로세스 종료 대기
            if (pid > 0)
            {
                Console.WriteLine($"[A2Updater] PID {pid} 종료 대기...");
                try
                {
                    var proc = Process.GetProcessById(pid);
                    proc.WaitForExit(10_000);
                }
                catch (ArgumentException) { /* already exited */ }
            }

            // 2. 기존 파일 삭제 (재시도)
            Console.WriteLine($"[A2Updater] 기존 파일 삭제: {target}");
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    break;
                }
                catch (IOException) when (attempt < 19)
                {
                    await Task.Delay(500);
                }
            }

            if (File.Exists(target))
            {
                Console.Error.WriteLine("[A2Updater] 파일 삭제 실패");
                return 2;
            }

            // 3. 새 버전 다운로드
            Console.WriteLine($"[A2Updater] 다운로드 중: {url}");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.Add("User-Agent", "A2Updater");

            var bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(target, bytes);
            Console.WriteLine($"[A2Updater] 다운로드 완료 ({bytes.Length:N0} bytes)");

            // 4. 재실행
            Console.WriteLine("[A2Updater] 본체 재실행");
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });

            Console.WriteLine("[A2Updater] 완료");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[A2Updater] 오류: {ex.Message}");
            return 3;
        }
    }
}
