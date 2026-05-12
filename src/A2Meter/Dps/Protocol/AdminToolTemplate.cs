namespace A2Meter.Dps.Protocol;

internal static class AdminToolTemplate
{
    public const string HtmlContent = """
<!DOCTYPE html>
<html lang="ko">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>A2Meter Premium Combat Log Analyzer (Admin)</title>
    <!-- Google Fonts -->
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;600;700&family=Noto+Sans+KR:wght@300;400;500;700&display=swap" rel="stylesheet">
    <!-- Tailwind CSS (Tailored Visuals) -->
    <script src="https://cdn.tailwindcss.com"></script>
    <!-- Chart.js -->
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <script>
        tailwind.config = {
            theme: {
                extend: {
                    fontFamily: {
                        sans: ['Outfit', 'Noto Sans KR', 'sans-serif'],
                    }
                }
            }
        }
    </script>
    <style>
        body {
            background-color: #0b0f19;
            color: #f3f4f6;
            background-image: 
                radial-gradient(at 0% 0%, rgba(31, 41, 234, 0.07) 0, transparent 50%), 
                radial-gradient(at 100% 100%, rgba(139, 92, 246, 0.07) 0, transparent 50%);
        }
        .glass-panel {
            background: rgba(17, 24, 39, 0.7);
            backdrop-filter: blur(12px);
            border: 1px solid rgba(255, 255, 255, 0.05);
            box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.37);
        }
        .glass-card {
            background: rgba(31, 41, 55, 0.4);
            border: 1px solid rgba(255, 255, 255, 0.03);
            transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
        }
        .glass-card:hover {
            transform: translateY(-2px);
            border-color: rgba(255, 255, 255, 0.08);
            background: rgba(31, 41, 55, 0.6);
            box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.3);
        }
        ::-webkit-scrollbar {
            width: 8px;
            height: 8px;
        }
        ::-webkit-scrollbar-track {
            background: #0b0f19;
        }
        ::-webkit-scrollbar-thumb {
            background: #1f293d;
            border-radius: 4px;
        }
        ::-webkit-scrollbar-thumb:hover {
            background: #374151;
        }
    </style>
</head>
<body class="min-h-screen p-6 font-sans transition-all duration-300">

    <!-- Header -->
    <header class="max-w-7xl mx-auto mb-8 flex flex-col md:flex-row md:items-center md:justify-between gap-4 border-b border-gray-800 pb-6">
        <div>
            <div class="flex items-center gap-3">
                <span class="px-2.5 py-1 text-xs font-bold uppercase tracking-wider bg-gradient-to-r from-cyan-500 to-blue-600 text-white rounded shadow-sm shadow-cyan-500/20">Admin Mode</span>
                <h1 class="text-3xl font-extrabold tracking-tight bg-clip-text text-transparent bg-gradient-to-r from-white via-gray-100 to-gray-400">A2Meter Combat Analyzer</h1>
            </div>
            <p class="text-gray-400 text-sm mt-1">로컬 전투 로그 JSON 파일을 드래그 앤 드롭하여 화려한 비주얼로 정밀 진단하세요.</p>
        </div>
        <div class="flex items-center gap-4">
            <label class="cursor-pointer flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-blue-600 to-violet-600 hover:from-blue-500 hover:to-violet-500 text-white text-sm font-semibold rounded-lg shadow-lg shadow-violet-500/20 transition duration-200">
                <span>📁 파일 선택하기</span>
                <input type="file" id="fileInput" accept=".json" class="hidden">
            </label>
        </div>
    </header>

    <main class="max-w-7xl mx-auto space-y-8">
        <!-- Drag & Drop Area -->
        <div id="dropZone" class="glass-panel border-2 border-dashed border-gray-700 hover:border-cyan-500 rounded-2xl p-12 text-center transition-all duration-300 cursor-pointer flex flex-col items-center justify-center gap-3 group">
            <div class="w-16 h-16 rounded-full bg-cyan-950/40 border border-cyan-800/50 flex items-center justify-center text-cyan-400 text-3xl group-hover:scale-110 transition duration-300">📥</div>
            <p class="text-lg font-medium text-gray-200">분석할 전투 로그 JSON 파일을 이리로 던져주세요</p>
            <p class="text-sm text-gray-400">또는 컴퓨터에서 생성된 <code class="px-1.5 py-0.5 bg-gray-900 rounded font-mono text-cyan-300 text-xs">combat_*.json</code> 파일을 끌어다 놓으세요.</p>
        </div>

        <!-- Analyzer Content Wrapper (Hidden Initially) -->
        <div id="analyzerContent" class="hidden space-y-8">
            <!-- Summary Overview Cards -->
            <section class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <div class="glass-card rounded-xl p-5 flex flex-col justify-between min-h-[110px]">
                    <span class="text-xs font-semibold text-gray-400 uppercase tracking-wider">🎯 공략 타겟 (Boss)</span>
                    <h3 id="summBoss" class="text-2xl font-bold mt-2 text-cyan-400 tracking-tight">-</h3>
                </div>
                <div class="glass-card rounded-xl p-5 flex flex-col justify-between min-h-[110px]">
                    <span class="text-xs font-semibold text-gray-400 uppercase tracking-wider">⚔️ 전투 진행 시간</span>
                    <h3 id="summDuration" class="text-2xl font-bold mt-2 text-violet-400 tracking-tight">-</h3>
                </div>
                <div class="glass-card rounded-xl p-5 flex flex-col justify-between min-h-[110px]">
                    <span class="text-xs font-semibold text-gray-400 uppercase tracking-wider">💥 파티 누적 피해량</span>
                    <h3 id="summDamage" class="text-2xl font-bold mt-2 text-rose-400 tracking-tight">-</h3>
                </div>
                <div class="glass-card rounded-xl p-5 flex flex-col justify-between min-h-[110px]">
                    <span class="text-xs font-semibold text-gray-400 uppercase tracking-wider">⚡ 파티 평균 DPS</span>
                    <h3 id="summDps" class="text-2xl font-bold mt-2 text-emerald-400 tracking-tight">-</h3>
                </div>
            </section>

            <!-- Charts Section -->
            <section class="grid grid-cols-1 lg:grid-cols-12 gap-6">
                <!-- Share Chart -->
                <div class="glass-panel rounded-2xl p-6 lg:col-span-5 flex flex-col h-[400px]">
                    <h2 class="text-lg font-bold mb-4 text-gray-200 border-b border-gray-800 pb-2 flex items-center gap-2">📊 파티원 딜 지분 점유율</h2>
                    <div class="flex-1 relative flex items-center justify-center p-4 min-h-0">
                        <canvas id="shareChart"></canvas>
                    </div>
                </div>
                <!-- Damage Bar Chart -->
                <div class="glass-panel rounded-2xl p-6 lg:col-span-7 flex flex-col h-[400px]">
                    <h2 class="text-lg font-bold mb-4 text-gray-200 border-b border-gray-800 pb-2 flex items-center gap-2">📈 딜러별 누적 피해량</h2>
                    <div class="flex-1 relative min-h-0">
                        <canvas id="damageChart"></canvas>
                    </div>
                </div>
            </section>

            <!-- Roster Detail Section -->
            <section class="glass-panel rounded-2xl p-6">
                <h2 class="text-lg font-bold mb-4 text-gray-200 border-b border-gray-800 pb-2 flex items-center gap-2">🛡️ 파티원 전투 통계 및 상세 데이터</h2>
                <div class="overflow-x-auto">
                    <table class="w-full text-left border-collapse">
                        <thead>
                            <tr class="text-xs font-semibold text-gray-400 uppercase tracking-wider border-b border-gray-800">
                                <th class="pb-3 font-medium">닉네임</th>
                                <th class="pb-3 font-medium">직업</th>
                                <th class="pb-3 font-medium text-right">누적 피해량</th>
                                <th class="pb-3 font-medium text-right">딜 지분</th>
                                <th class="pb-3 font-medium text-right">실시간 DPS</th>
                                <th class="pb-3 font-medium text-right">치명타 확률</th>
                                <th class="pb-3 font-medium text-right">전투력 (CP)</th>
                                <th class="pb-3 font-medium text-center">액션</th>
                            </tr>
                        </thead>
                        <tbody id="rosterTableBody" class="text-sm divide-y divide-gray-800/40 text-gray-300">
                            <!-- Populated dynamically -->
                        </tbody>
                    </table>
                </div>
            </section>

            <!-- Skill Detail Modal/Overlay -->
            <section id="skillSection" class="hidden glass-panel rounded-2xl p-6 transition-all duration-300">
                <div class="flex items-center justify-between border-b border-gray-800 pb-3 mb-4">
                    <h3 class="text-lg font-bold flex items-center gap-2">
                        <span id="selectedActorJob" class="px-2 py-0.5 rounded text-xs text-white">-</span>
                        <span id="selectedActorName" class="text-gray-100">-</span>님의 스킬 기여도 상세 분석
                    </h3>
                    <button onclick="closeSkills()" class="text-gray-400 hover:text-white font-bold text-lg px-2">✕</button>
                </div>
                <div class="grid grid-cols-1 lg:grid-cols-12 gap-6">
                    <div class="lg:col-span-4 flex items-center justify-center p-4 min-h-[250px] max-h-[300px]">
                        <canvas id="skillPieChart"></canvas>
                    </div>
                    <div class="lg:col-span-8 overflow-x-auto">
                        <table class="w-full text-left border-collapse">
                            <thead>
                                <tr class="text-xs font-semibold text-gray-400 uppercase border-b border-gray-800">
                                    <th class="pb-2 font-medium">스킬명</th>
                                    <th class="pb-2 font-medium text-right">누적 피해량</th>
                                    <th class="pb-2 font-medium text-right">비율 (%)</th>
                                    <th class="pb-2 font-medium text-right">타격수</th>
                                    <th class="pb-2 font-medium text-right">최대 데미지</th>
                                    <th class="pb-2 font-medium text-right">치명타율</th>
                                </tr>
                            </thead>
                            <tbody id="skillTableBody" class="text-xs divide-y divide-gray-800/40 text-gray-300">
                                <!-- Populated dynamically -->
                            </tbody>
                        </table>
                    </div>
                </div>
            </section>
        </div>
    </main>

    <footer class="max-w-7xl mx-auto mt-16 pt-6 border-t border-gray-800 text-center text-xs text-gray-500">
        A2Meter Admin Suite &copy; 2026. All rights reserved. Self-contained log parsing & analysis kit.
    </footer>

    <!-- JS Logic -->
    <script>
        const dropZone = document.getElementById('dropZone');
        const fileInput = document.getElementById('fileInput');
        const analyzerContent = document.getElementById('analyzerContent');
        const skillSection = document.getElementById('skillSection');
        
        // Colors mapping based on UI archetype
        const jobColors = {
            '검성': '#86DDF3',
            '궁성': '#62B18F',
            '마도성': '#B78CF2',
            '살성': '#A4E79B',
            '수호성': '#7DA0F9',
            '정령성': '#CF6BD0',
            '치유성': '#E7CF7D',
            '호법성': '#E4A55B'
        };

        let currentData = null;
        let shareChartInstance = null;
        let damageChartInstance = null;
        let skillPieChartInstance = null;

        // Event Listeners
        dropZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            dropZone.classList.add('border-cyan-500', 'bg-cyan-950/10');
        });

        dropZone.addEventListener('dragleave', () => {
            dropZone.classList.remove('border-cyan-500', 'bg-cyan-950/10');
        });

        dropZone.addEventListener('drop', (e) => {
            e.preventDefault();
            dropZone.classList.remove('border-cyan-500', 'bg-cyan-950/10');
            const file = e.dataTransfer.files[0];
            if (file && file.name.endsWith('.json')) {
                parseFile(file);
            }
        });

        dropZone.addEventListener('click', () => fileInput.click());
        fileInput.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (file) parseFile(file);
        });

        function parseFile(file) {
            const reader = new FileReader();
            reader.onload = function(e) {
                try {
                    const data = JSON.parse(e.target.result);
                    renderAnalyzer(data);
                } catch (err) {
                    alert('올바른 전투 기록 JSON 형식이 아닙니다: ' + err.message);
                }
            };
            reader.readAsText(file);
        }

        function formatNumber(num) {
            return Number(num).toLocaleString('ko-KR');
        }

        function formatTimer(sec) {
            const s = Math.max(0, parseInt(sec));
            return `${Math.floor(s / 60)}분 ${s % 60}초`;
        }

        function renderAnalyzer(data) {
            currentData = data;
            dropZone.classList.add('py-6');
            analyzerContent.classList.remove('hidden');
            skillSection.classList.add('hidden');

            // 1. Render Overviews
            document.getElementById('summBoss').textContent = data.BossName || '필드 전투';
            document.getElementById('summDuration').textContent = formatTimer(data.DurationSec || 0);
            document.getElementById('summDamage').textContent = formatNumber(data.TotalDamage || 0);
            document.getElementById('summDps').textContent = formatNumber(data.AverageDps || 0);

            // Fetch players
            const players = (data.Snapshot && data.Snapshot.Players) || [];
            // Sort by damage descending
            players.sort((a, b) => b.TotalDamage - a.TotalDamage);

            // 2. Render Share & Damage Charts
            renderCharts(players);

            // 3. Render Table
            const tbody = document.getElementById('rosterTableBody');
            tbody.innerHTML = '';

            players.forEach(p => {
                const tr = document.createElement('tr');
                tr.className = 'hover:bg-gray-800/20 transition duration-150';

                const jobName = p.ServerName || '일반';
                const pct = (p.DamagePercent * 100).toFixed(1);

                tr.innerHTML = `
                    <td class="py-3.5 font-semibold text-gray-100">${p.Name}</td>
                    <td class="py-3.5">
                        <span class="px-2 py-0.5 rounded text-xs font-semibold" style="background: ${jobColors[p.ServerName] || '#4b5563'}44; color: ${jobColors[p.ServerName] || '#e5e7eb'}">
                            ${jobName}
                        </span>
                    </td>
                    <td class="py-3.5 text-right font-medium">${formatNumber(p.TotalDamage)}</td>
                    <td class="py-3.5 text-right font-semibold text-cyan-400">${pct}%</td>
                    <td class="py-3.5 text-right">${formatNumber(p.Dps)}</td>
                    <td class="py-3.5 text-right text-yellow-500">${(p.CritRate * 100).toFixed(1)}%</td>
                    <td class="py-3.5 text-right font-mono text-xs text-gray-400">${formatNumber(p.CombatPower || 0)}</td>
                    <td class="py-3.5 text-center">
                        <button onclick="viewSkills('${p.Name}')" class="px-3 py-1 bg-gray-800 hover:bg-gray-700 text-cyan-300 rounded text-xs font-medium transition duration-150">🔍 상세 분석</button>
                    </td>
                `;
                tbody.appendChild(tr);
            });
        }

        function renderCharts(players) {
            if (shareChartInstance) shareChartInstance.destroy();
            if (damageChartInstance) damageChartInstance.destroy();

            const labels = players.map(p => p.Name);
            const damages = players.map(p => p.TotalDamage);
            const backgroundColors = players.map(p => {
                const color = jobColors[p.ServerName] || '#4b5563';
                return color + 'cc';
            });

            // Share Chart
            const ctxShare = document.getElementById('shareChart').getContext('2d');
            shareChartInstance = new Chart(ctxShare, {
                type: 'doughnut',
                data: {
                    labels: labels,
                    datasets: [{
                        data: damages,
                        backgroundColor: backgroundColors,
                        borderWidth: 1,
                        borderColor: '#0b0f19'
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            position: 'bottom',
                            labels: { color: '#f3f4f6', font: { family: 'Outfit' } }
                        }
                    }
                }
            });

            // Damage Chart
            const ctxDamage = document.getElementById('damageChart').getContext('2d');
            damageChartInstance = new Chart(ctxDamage, {
                type: 'bar',
                data: {
                    labels: labels,
                    datasets: [{
                        label: '누적 피해량 (Damage)',
                        data: damages,
                        backgroundColor: backgroundColors,
                        borderWidth: 0,
                        borderRadius: 4
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    scales: {
                        x: { grid: { display: false }, ticks: { color: '#f3f4f6', font: { family: 'Outfit' } } },
                        y: { grid: { color: '#1f293d' }, ticks: { color: '#9ca3af', font: { family: 'Outfit' } } }
                    },
                    plugins: {
                        legend: { display: false }
                    }
                }
            });
        }

        function viewSkills(playerName) {
            if (!currentData) return;
            const players = (currentData.Snapshot && currentData.Snapshot.Players) || [];
            const actor = players.find(p => p.Name === playerName);
            if (!actor) return;

            document.getElementById('selectedActorName').textContent = actor.Name;
            const jobBadge = document.getElementById('selectedActorJob');
            const jobName = actor.ServerName || '일반';
            jobBadge.textContent = jobName;
            const color = jobColors[actor.ServerName] || '#4b5563';
            jobBadge.style.backgroundColor = color;

            const skills = actor.TopSkills || [];
            skills.sort((a, b) => b.Total - a.Total);

            const tbody = document.getElementById('skillTableBody');
            tbody.innerHTML = '';

            const pieLabels = [];
            const pieData = [];
            const pieColors = [];

            const totalActorDamage = actor.TotalDamage || 1;

            skills.forEach((s, idx) => {
                const ratio = ((s.Total / totalActorDamage) * 100).toFixed(1);
                const tr = document.createElement('tr');
                tr.className = 'hover:bg-gray-800/10 transition';
                tr.innerHTML = `
                    <td class="py-2.5 font-semibold text-gray-200">${s.Name}</td>
                    <td class="py-2.5 text-right font-medium">${formatNumber(s.Total)}</td>
                    <td class="py-2.5 text-right font-semibold text-cyan-400">${ratio}%</td>
                    <td class="py-2.5 text-right text-gray-400">${formatNumber(s.Hits)}</td>
                    <td class="py-2.5 text-right font-mono text-gray-300">${formatNumber(s.MaxHit)}</td>
                    <td class="py-2.5 text-right text-yellow-500">${(s.CritRate * 100).toFixed(1)}%</td>
                `;
                tbody.appendChild(tr);

                if (idx < 5) {
                    pieLabels.push(s.Name);
                    pieData.push(s.Total);
                    pieColors.push(hslColor(idx, skills.length));
                } else if (idx === 5) {
                    pieLabels.push('기타 스킬');
                    pieData.push(skills.slice(5).reduce((sum, item) => sum + item.Total, 0));
                    pieColors.push('#4b5563');
                }
            });

            if (skillPieChartInstance) skillPieChartInstance.destroy();
            const ctxSkill = document.getElementById('skillPieChart').getContext('2d');
            skillPieChartInstance = new Chart(ctxSkill, {
                type: 'pie',
                data: {
                    labels: pieLabels,
                    datasets: [{
                        data: pieData,
                        backgroundColor: pieColors,
                        borderWidth: 1,
                        borderColor: '#0b0f19'
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            position: 'right',
                            labels: { color: '#f3f4f6', font: { family: 'Outfit', size: 10 } }
                        }
                    }
                }
            });

            document.getElementById('skillSection').classList.remove('hidden');
            document.getElementById('skillSection').scrollIntoView({ behavior: 'smooth' });
        }

        function closeSkills() {
            document.getElementById('skillSection').classList.add('hidden');
        }

        function hslColor(index, total) {
            const hue = (index * (360 / Math.max(1, total))) % 360;
            return `hsla(${hue}, 70%, 60%, 0.8)`;
        }
    </script>
</body>
</html>
""";
}
