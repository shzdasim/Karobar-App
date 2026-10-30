<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <script>
      try {
        if (localStorage.getItem('theme_mode') === 'dark') {
          document.documentElement.classList.add('dark');
        }
      } catch (_) {}
    </script>
    <style>
      body { margin: 0; background-color: #f8fafc; color: #1e293b; font-family: system-ui, -apple-system, sans-serif; }
      html.dark body { background-color: #0f172a; color: #f1f5f9; }
      .boot-shell { min-height: 100vh; padding: 14px; box-sizing: border-box; display: flex; flex-direction: column; gap: 14px; contain: layout style paint; }
      .boot-card { background: rgba(255,255,255,0.82); border: 1px solid rgba(226,232,240,0.8); border-radius: 16px; padding: 14px 16px; }
      html.dark .boot-card { background: rgba(30,41,59,0.78); border-color: rgba(51,65,85,0.7); }
      .boot-bone { border-radius: 8px; background: rgba(203,213,225,0.6); position: relative; overflow: hidden; }
      html.dark .boot-bone { background: rgba(51,65,85,0.6); }
      .boot-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
      .boot-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 12px; }
    </style>
    @viteReactRefresh
    @vite(['resources/css/app.css', 'resources/js/app.jsx'])
</head>
<body>
    <div id="app">
        <div class="boot-shell" aria-busy="true" aria-hidden="true">
            <div class="boot-card boot-row">
                <div style="display:flex;align-items:center;gap:12px;">
                    <div class="boot-bone" style="width:34px;height:34px;border-radius:10px;"></div>
                    <div class="boot-bone" style="width:160px;height:20px;"></div>
                </div>
                <div style="display:flex;align-items:center;gap:8px;">
                    <div class="boot-bone" style="width:34px;height:34px;border-radius:10px;"></div>
                    <div class="boot-bone" style="width:34px;height:34px;border-radius:10px;"></div>
                    <div class="boot-bone" style="width:96px;height:34px;border-radius:10px;"></div>
                </div>
            </div>
            <div class="boot-grid">
                <div class="boot-card"><div class="boot-bone" style="width:45%;height:14px;margin-bottom:10px;"></div><div class="boot-bone" style="width:70%;height:26px;"></div></div>
                <div class="boot-card"><div class="boot-bone" style="width:45%;height:14px;margin-bottom:10px;"></div><div class="boot-bone" style="width:70%;height:26px;"></div></div>
                <div class="boot-card"><div class="boot-bone" style="width:45%;height:14px;margin-bottom:10px;"></div><div class="boot-bone" style="width:70%;height:26px;"></div></div>
                <div class="boot-card"><div class="boot-bone" style="width:45%;height:14px;margin-bottom:10px;"></div><div class="boot-bone" style="width:70%;height:26px;"></div></div>
            </div>
            <div class="boot-card" style="flex:1;display:flex;flex-direction:column;gap:10px;">
                <div class="boot-bone" style="width:100%;height:36px;border-radius:10px;"></div>
                <div class="boot-bone" style="width:100%;height:28px;"></div>
                <div class="boot-bone" style="width:100%;height:28px;"></div>
                <div class="boot-bone" style="width:100%;height:28px;"></div>
                <div class="boot-bone" style="width:100%;height:28px;"></div>
                <div class="boot-bone" style="width:100%;height:28px;"></div>
            </div>
        </div>
    </div>
</body>
</html>
