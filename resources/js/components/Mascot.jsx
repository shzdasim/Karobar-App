import { useEffect, useRef, useState, useCallback } from 'react';
import axios from 'axios';

export default function Mascot() {
  const mascotRef = useRef(null);
  const boxRef = useRef(null);
  const [mousePos, setMousePos] = useState({ x: 0, y: 0 });
  const [eyeOffset, setEyeOffset] = useState({ x: 0, y: 0 });
  const [isBlinking, setIsBlinking] = useState(false);
  const [bouncePhase, setBouncePhase] = useState(0);
  const [squish, setSquish] = useState(0);
  const [mouthState, setMouthState] = useState('neutral');
  const [showThinking, setShowThinking] = useState(false);
  const [showSales, setShowSales] = useState(false);
  const [clickCount, setClickCount] = useState(0);
  const [salesData, setSalesData] = useState(null);
  // Local calendar date (toISOString() would give the UTC date, which can be
  // yesterday's date during the first hours of the local day)
  const today = (() => {
    const d = new Date();
    const pad = (n) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  })();
  const [showPurchase, setShowPurchase] = useState(false);
  const [purchaseData, setPurchaseData] = useState(null);
  const [showProfit, setShowProfit] = useState(false);
  const [profitData, setProfitData] = useState(null);

  // Track mouse
  useEffect(() => {
    const handleMouseMove = (e) => {
      setMousePos({ x: e.clientX, y: e.clientY });
    };
    const handleTouchMove = (e) => {
      if (e.touches.length > 0) {
        setMousePos({ x: e.touches[0].clientX, y: e.touches[0].clientY });
      }
    };
    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('touchmove', handleTouchMove);
    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('touchmove', handleTouchMove);
    };
  }, []);

  // Eye follows cursor
  useEffect(() => {
    if (!mascotRef.current) return;
    const rect = mascotRef.current.getBoundingClientRect();
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2 - 5;
    const dx = mousePos.x - centerX;
    const dy = mousePos.y - centerY;
    const dist = Math.sqrt(dx * dx + dy * dy);
    const maxShift = 4;
    if (dist > 0) {
      setEyeOffset({
        x: Math.min(Math.max((dx / dist) * maxShift, -maxShift), maxShift),
        y: Math.min(Math.max((dy / dist) * maxShift, -maxShift), maxShift),
      });
    } else {
      setEyeOffset({ x: 0, y: 0 });
    }
  }, [mousePos]);

  // Periodic blink
  useEffect(() => {
    let blinkTimer;
    let nextBlinkTimer;
    const doBlink = () => {
      setIsBlinking(true);
      blinkTimer = setTimeout(() => {
        setIsBlinking(false);
        nextBlinkTimer = setTimeout(doBlink, 3000 + Math.random() * 2000);
      }, 120);
    };
    const firstBlink = setTimeout(doBlink, 1500);
    return () => {
      clearTimeout(firstBlink);
      clearTimeout(blinkTimer);
      clearTimeout(nextBlinkTimer);
    };
  }, []);

  // Mouth changes
  useEffect(() => {
    let timer;
    const changeMouth = () => {
      const states = ['neutral', 'smiling', 'frowning', 'closed'];
      const randomState = states[Math.floor(Math.random() * states.length)];
      setMouthState(randomState);
      timer = setTimeout(changeMouth, 5000 + Math.random() * 5000);
    };
    const firstChange = setTimeout(changeMouth, 3000);
    return () => {
      clearTimeout(firstChange);
      clearTimeout(timer);
    };
  }, []);

  // Bounce
  useEffect(() => {
    const timer = setInterval(() => {
      setBouncePhase((p) => (p + 1) % 2);
    }, 1500);
    return () => clearInterval(timer);
  }, []);

  // Hover squish
  useEffect(() => {
    if (squish > 0) {
      const t = setTimeout(() => setSquish(0), 300);
      return () => clearTimeout(t);
    }
  }, [squish]);

  const onEnter = useCallback(() => setSquish(1), []);
  const onLeave = useCallback(() => setSquish(0), []);
  const nearMouse = mousePos.x > 0 && mousePos.y > 0;

  // Handle click - show thinking then fetch and show sales data in bubbles
  const handleClick = useCallback(async () => {
    setClickCount((prev) => prev + 1);
    setShowThinking(true);
    setShowSales(false);
    setShowPurchase(false);
    setShowProfit(false);
    setSalesData(null);
    setPurchaseData(null);
    setProfitData(null);
    
    // Brief thinking delay
    await new Promise((r) => setTimeout(r, 600));
    setShowThinking(false);

    // Profit bubble is only shown when the cost-of-sale report is available
    let profitReady = false;

    // Today's sales & purchases (credit / debit split) from the dashboard
    try {
      const { data } = await axios.get('/api/dashboard/summary');
      const totals = data?.totals ?? {};

      const salesCredit = totals?.sales_credit ?? 0;
      const salesDebit = totals?.sales_debit ?? 0;
      const totalSales = totals?.sales ?? 0;

      const purchasesCredit = totals?.purchases_credit ?? 0;
      const purchasesDebit = totals?.purchases_debit ?? 0;
      const totalPurchases = totals?.purchases ?? 0;

      setSalesData({
        credit: salesCredit ? `R.S. ${Number(salesCredit).toLocaleString()}` : 'R.S. 0',
        debit: salesDebit ? `R.S. ${Number(salesDebit).toLocaleString()}` : 'R.S. 0',
        total: totalSales ? `R.S. ${Number(totalSales).toLocaleString()}` : 'R.S. 0',
      });

      setPurchaseData({
        credit: purchasesCredit ? `R.S. ${Number(purchasesCredit).toLocaleString()}` : 'R.S. 0',
        debit: purchasesDebit ? `R.S. ${Number(purchasesDebit).toLocaleString()}` : 'R.S. 0',
        total: totalPurchases ? `R.S. ${Number(totalPurchases).toLocaleString()}` : 'R.S. 0',
      });
    } catch (err) {
      setSalesData({
        credit: 'R.S. 0',
        debit: 'R.S. 0',
        total: 'R.S. 0',
      });
      setPurchaseData({
        credit: 'R.S. 0',
        debit: 'R.S. 0',
        total: 'R.S. 0',
      });
    }

    // Profit - exact formula from ReportsController.php (costOfSale method),
    // same derivation as pages/Reports/CostOfSaleReport.jsx:
    //   net_sale      = total_sales - sale_return        (from the report rows)
    //   cost_of_sales = COGS on sales - COGS reversed on returns (server-side)
    //   gp_amount     = net_sale - cost_of_sales
    //   gp_pct        = (gp_amount / net_sale) * 100
    // All three inputs come from the same report response so the numbers
    // always match the Cost of Sale report for the same date.
    try {
      const res = await axios.get('/api/reports/cost-of-sale', {
        params: { from: today, to: today },
      });
      const rows = Array.isArray(res?.data)
        ? res.data
        : Array.isArray(res?.data?.data)
        ? res.data.data
        : [];

      const reportTotalSales = rows.reduce((sum, r) => sum + Number(r.total_sales || 0), 0);
      const saleReturns = rows.reduce((sum, r) => sum + Number(r.sale_return || 0), 0);
      const costOfSales = rows.reduce((sum, r) => sum + Number(r.cost_of_sales || 0), 0);

      const netSale = reportTotalSales - saleReturns;
      const gpAmount = netSale - costOfSales;
      const gpPct = netSale > 0 ? (gpAmount / netSale) * 100 : 0;

      setProfitData({
        netSales: `R.S. ${Number(netSale).toLocaleString()}`,
        costOfSales: `R.S. ${Number(costOfSales).toLocaleString()}`,
        profit: `R.S. ${Number(gpAmount).toLocaleString()}`,
        profitMargin: `${gpPct.toFixed(2)}%`,
      });
      profitReady = true;
    } catch (err) {
      // Endpoint requires the report.cost-of-sale.view permission;
      // without it no COGS can be derived, so hide the profit bubble
      // instead of showing an inflated profit.
      setProfitData(null);
      profitReady = false;
    }

    // Show bubbles
    setShowSales(true);
    setShowPurchase(true);
    setShowProfit(profitReady);
    
    // Reset after 5 seconds
    setTimeout(() => {
      setShowSales(false);
      setShowPurchase(false);
      setShowProfit(false);
      setSalesData(null);
      setPurchaseData(null);
      setProfitData(null);
      setClickCount(0);
    }, 5000);
  }, [today]);

  return (
    <div style={{
      position: 'fixed',
      bottom: '24px',
      left: '50%',
      transform: 'translateX(-50%)',
      zIndex: 9999,
      pointerEvents: 'auto',
      userSelect: 'none',
    }}>
      <div
        ref={boxRef}
        className={`box ${squish > 0 ? 'squish' : ''}`}
        style={{
          position: 'relative',
          width: '90px',
          height: '80px',
          background: '#0a0a0a',
          borderRadius: '8px',
          border: '2px solid rgba(255,255,255,0.15)',
          boxShadow: `
            0 4px 20px rgba(0,0,0,0.4),
            inset 0 1px 0 rgba(255,255,255,0.1),
            0 0 40px rgba(255,255,255,${nearMouse ? 0.08 : 0.03})
          `,
          transform: `translateY(${bouncePhase === 0 ? 0 : -3}px)${squish > 0 ? ' scaleY(0.9)' : ''}`,
          transition: 'transform 0.15s ease',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: 14,
          cursor: 'pointer',
        }}
        onMouseEnter={onEnter}
        onMouseLeave={onLeave}
        onClick={handleClick}
      >
        {/* Left eye */}
        <Eye
          offsetX={eyeOffset.x}
          offsetY={eyeOffset.y}
          blinking={isBlinking}
        />

        {/* Right eye */}
        <Eye
          offsetX={eyeOffset.x}
          offsetY={eyeOffset.y}
          blinking={isBlinking}
        />

        {/* Mouth */}
        <div className={`mouth mouth-${mouthState}`}>
          <svg viewBox="0 0 24 16">
            {mouthState === 'smiling' && (
              <>
                <path d="M3 10 Q12 18 21 10" fill="none" stroke="rgba(255,255,255,0.9)" strokeWidth="2" strokeLinecap="round" />
                <path d="M8 11 L8 13.5 Q10 14 12 13.5 L12 11" fill="rgba(255,255,255,0.85)" />
                <path d="M6.5 12.5 L7.5 11" stroke="rgba(255,255,255,0.7)" strokeWidth="1.5" strokeLinecap="round" />
                <path d="M14.5 12.5 L13.5 11" stroke="rgba(255,255,255,0.7)" strokeWidth="1.5" strokeLinecap="round" />
              </>
            )}
            {mouthState === 'frowning' && (
              <path d="M3 8 Q12 2 21 8" fill="none" stroke="rgba(255,255,255,0.9)" strokeWidth="2" strokeLinecap="round" />
            )}
            {mouthState === 'closed' && (
              <path d="M4 8 L20 8" stroke="rgba(255,255,255,0.9)" strokeWidth="2" strokeLinecap="round" />
            )}
            {mouthState === 'neutral' && (
              <path d="M6 9 Q12 12 18 9" fill="none" stroke="rgba(255,255,255,0.8)" strokeWidth="1.5" strokeLinecap="round" />
            )}
          </svg>
        </div>

        {/* Thinking bubble at top of head */}
        {showThinking && (
          <div className="thinking-bubble">
            <div className="thinking-dots">
              <span />
              <span />
              <span />
            </div>
          </div>
        )}

        {/* Sales bubbles - credit and debit */}
        {showSales && salesData && (
          <>
            <div className="sales-bubble sales-bubble-left">
              <div className="bubble-stem" />
              <div className="bubble-content">
                <div className="bubble-title">Sales (Credit)</div>
                <div className="bubble-value" style={{ color: '#60a5fa' }}>{salesData.credit}</div>
              </div>
            </div>
            <div className="sales-bubble sales-bubble-right">
              <div className="bubble-stem" />
              <div className="bubble-content">
                <div className="bubble-title">Sales (Debit)</div>
                <div className="bubble-value" style={{ color: '#fbbf24' }}>{salesData.debit}</div>
              </div>
            </div>
          </>
        )}

        {/* Purchase bubbles - credit and debit */}
        {showPurchase && purchaseData && (
          <>
            <div className="sales-bubble sales-bubble-left purchase-bubble" style={{ top: '-60px' }}>
              <div className="bubble-stem" />
              <div className="bubble-content">
                <div className="bubble-title">Purchases (Credit)</div>
                <div className="bubble-value" style={{ color: '#a78bfa' }}>{purchaseData.credit}</div>
              </div>
            </div>
            <div className="sales-bubble sales-bubble-right purchase-bubble" style={{ top: '-60px' }}>
              <div className="bubble-stem" />
              <div className="bubble-content">
                <div className="bubble-title">Purchases (Debit)</div>
                <div className="bubble-value" style={{ color: '#f472b6' }}>{purchaseData.debit}</div>
              </div>
            </div>
          </>
        )}

        {/* Profit bubble */}
        {showProfit && profitData && (
          <div className="sales-bubble profit-bubble" style={{ top: '-105px' }}>
            <div className="bubble-stem" />
            <div className="bubble-content">
              <div className="bubble-title">Today's Profit</div>
              <div className="bubble-value" style={{ color: '#34d399' }}>{profitData.profit}</div>
              <div className="bubble-label">Net Sales: {profitData.netSales}</div>
              <div className="bubble-label">Cost of Sales: {profitData.costOfSales}</div>
              <div className="bubble-label">Margin: {profitData.profitMargin}</div>
            </div>
          </div>
        )}

        {/* Antenna */}
        <div className="antenna">
          <div className="antenna-line" />
          <div className="antenna-dot" />
        </div>
      </div>

      <style>{`
        .box {
          position: relative;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 14px;
          transition: transform 0.15s ease;
        }
        .box.squish {
          transform: translateY(-3px) scaleY(0.9);
        }

        .eye {
          position: relative;
          width: 24px;
          height: 24px;
          transform: translate(var(--ox, 0px), var(--oy, 0px));
          transition: transform 0.1s ease-out;
          will-change: transform;
        }

        .sclera {
          width: 24px;
          height: 24px;
          background: radial-gradient(circle at 35% 30%, #fffef5, #f5f5ff 60%, #e8e8f0);
          border-radius: 50%;
          box-shadow:
            inset 0 -3px 4px rgba(0,0,0,0.08),
            inset 0 3px 5px rgba(255,255,255,0.9),
            0 2px 8px rgba(0,0,0,0.15),
            0 0 12px rgba(255,255,255,0.15);
          display: flex;
          align-items: center;
          justify-content: center;
          position: relative;
          overflow: hidden;
        }

        .iris {
          width: 12px;
          height: 12px;
          background: radial-gradient(circle at 40% 35%, #1a1a1a, #000 70%);
          border-radius: 50%;
          display: flex;
          align-items: center;
          justify-content: center;
          position: relative;
          box-shadow:
            inset 0 1px 2px rgba(255,255,255,0.15),
            0 0 6px rgba(0,0,0,0.4);
        }

        .pupil {
          width: 6px;
          height: 6px;
          background: #000;
          border-radius: 50%;
          position: absolute;
          top: 50%;
          left: 50%;
          transform: translate(-50%, -50%);
        }

        .iris::before {
          content: '';
          position: absolute;
          top: 2px;
          left: 3px;
          width: 5px;
          height: 5px;
          background: white;
          border-radius: 50%;
          opacity: 0.95;
          animation: twinkle-main 1.8s ease-in-out infinite;
          box-shadow: 0 0 3px rgba(255,255,255,0.8);
        }

        .iris::after {
          content: '';
          position: absolute;
          bottom: 2px;
          right: 2px;
          width: 2.5px;
          height: 2.5px;
          background: white;
          border-radius: 50%;
          opacity: 0.7;
          animation: twinkle-secondary 2.2s ease-in-out infinite;
          animation-delay: 0.3s;
        }

        .sparkle-tiny {
          position: absolute;
          top: 1px;
          right: 3px;
          width: 2px;
          height: 2px;
          background: white;
          border-radius: 50%;
          opacity: 0.8;
          animation: twinkle-tiny 1.5s ease-in-out infinite;
          animation-delay: 0.7s;
        }

        .sparkle-1 {
          position: absolute;
          top: -4px;
          right: -4px;
          width: 3px;
          height: 3px;
          background: white;
          border-radius: 50%;
          opacity: 0;
          animation: float-sparkle 2.5s ease-in-out infinite;
        }
        .sparkle-2 {
          position: absolute;
          bottom: -5px;
          left: -3px;
          width: 2px;
          height: 2px;
          background: white;
          border-radius: 50%;
          opacity: 0;
          animation: float-sparkle 3s ease-in-out infinite;
          animation-delay: 0.8s;
        }
        .sparkle-3 {
          position: absolute;
          top: 6px;
          left: -6px;
          width: 2.5px;
          height: 2.5px;
          background: white;
          border-radius: 50%;
          opacity: 0;
          animation: float-sparkle 2.8s ease-in-out infinite;
          animation-delay: 1.2s;
        }

        .glow {
          position: absolute;
          top: 50%;
          left: 50%;
          width: 32px;
          height: 32px;
          transform: translate(-50%, -50%);
          background: radial-gradient(circle, rgba(255,255,255,0.25), transparent 70%);
          border-radius: 50%;
          pointer-events: none;
          animation: eye-glow 3s ease-in-out infinite;
        }

        .eye.blinking .sclera {
          animation: eye-blink 120ms ease-in-out;
        }

        @keyframes eye-blink {
          0% { transform: scaleY(1); }
          50% { transform: scaleY(0.05); }
          100% { transform: scaleY(1); }
        }

        @keyframes twinkle-main {
          0%, 100% { opacity: 0.9; transform: scale(1); }
          50% { opacity: 1; transform: scale(1.2); }
        }

        @keyframes twinkle-secondary {
          0%, 100% { opacity: 0.5; transform: scale(0.8); }
          50% { opacity: 0.9; transform: scale(1.2); }
        }

        @keyframes twinkle-tiny {
          0%, 100% { opacity: 0.4; transform: scale(0.7); }
          50% { opacity: 1; transform: scale(1.3); }
        }

        @keyframes float-sparkle {
          0%, 100% { opacity: 0; transform: translateY(0) scale(0.5); }
          20% { opacity: 1; transform: translateY(-3px) scale(1); }
          40% { opacity: 0.6; transform: translateY(-6px) scale(0.8); }
          60% { opacity: 0; transform: translateY(-9px) scale(0.5); }
        }

        @keyframes eye-glow {
          0%, 100% { opacity: 0.6; transform: scale(1); }
          50% { opacity: 1; transform: scale(1.15); }
        }

        .mouth {
          position: absolute;
          bottom: 10px;
          left: 50%;
          transform: translateX(-50%);
          width: 24px;
          height: 16px;
          transition: transform 0.3s ease;
        }
        .mouth svg {
          width: 100%;
          height: 100%;
        }
        .mouth-smiling { transform: translateX(-50%) scale(1.05); }
        .mouth-frowning { transform: translateX(-50%) scale(1.02); }
        .mouth-closed { transform: translateX(-50%); }
        .mouth-neutral { transform: translateX(-50%); }

        /* Thinking bubble - appears at top of head */
        .thinking-bubble {
          position: absolute;
          top: -30px;
          left: 50%;
          transform: translateX(-50%);
          width: 30px;
          height: 20px;
          background: rgba(20, 25, 40, 0.9);
          border: 1px solid rgba(255,255,255,0.2);
          border-radius: 15px;
          display: flex;
          flex-direction: column;
          align-items: center;
          justify-content: center;
          gap: 3px;
          animation: bubble-in 0.2s ease-out;
        }

        .thinking-dots {
          display: flex;
          gap: 4px;
        }

        .thinking-dots span {
          width: 5px;
          height: 5px;
          background: rgba(255,255,255,0.8);
          border-radius: 50%;
          animation: think-bounce 1s ease-in-out infinite;
        }

        .thinking-dots span:nth-child(2) {
          animation-delay: 0.2s;
        }
        .thinking-dots span:nth-child(3) {
          animation-delay: 0.4s;
        }

        @keyframes think-bounce {
          0%, 100% { transform: translateY(0); opacity: 0.5; }
          50% { transform: translateY(-3px); opacity: 1; }
        }

        @keyframes bubble-in {
          from { opacity: 0; transform: translateX(-50%) scale(0.8); }
          to { opacity: 1; transform: translateX(-50%) scale(1); }
        }

        /* Sales bubbles - positioned on left and right */
        .sales-bubble {
          position: absolute;
          width: 70px;
          background: rgba(20, 25, 40, 0.95);
          backdrop-filter: blur(10px);
          border: 1px solid rgba(255,255,255,0.2);
          border-radius: 10px;
          padding: 8px 6px;
          animation: sales-pop 0.3s ease-out;
          box-shadow: 0 6px 16px rgba(0,0,0,0.3);
          z-index: 100;
          text-align: center;
        }

        .sales-bubble-left {
          left: -50px;
          transform: translateX(-50%);
        }

        .sales-bubble-right {
          right: -50px;
          transform: translateX(50%);
        }

        .sales-bubble.profit-bubble {
          left: 50%;
          transform: translateX(-50%);
          top: -110px;
          width: 80px;
        }

        .purchase-bubble {
          top: -55px !important;
        }

        .bubble-stem {
          position: absolute;
          bottom: -6px;
          left: 50%;
          transform: translateX(-50%);
          width: 2px;
          height: 6px;
          background: rgba(255,255,255,0.2);
          border-radius: 1px;
        }

        .sales-bubble-left .bubble-stem {
          right: -6px;
          left: auto;
        }

        .sales-bubble-right .bubble-stem {
          left: -6px;
          right: auto;
        }

        .bubble-content {
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 2px;
        }

        .bubble-title {
          color: rgba(255,255,255,0.6);
          font-size: 8px;
          font-weight: 500;
          text-transform: uppercase;
          letter-spacing: 0.3px;
          margin-bottom: 2px;
        }

        .bubble-value {
          color: #fff;
          font-size: 12px;
          font-weight: 700;
        }

        @keyframes sales-pop {
          0% { opacity: 0; transform: translateX(-50%) translateY(8px) scale(0.9); }
          100% { opacity: 1; transform: translateX(-50%) translateY(0) scale(1); }
        }

        .sales-bubble-right .sales-bubble {
          animation-name: sales-pop-right;
        }

        @keyframes sales-pop-right {
          0% { opacity: 0; transform: translateX(50%) translateY(8px) scale(0.9); }
          100% { opacity: 1; transform: translateX(50%) translateY(0) scale(1); }
        }

        @keyframes sales-pop {
          0% { opacity: 0; transform: translateX(-50%) translateY(10px) scale(0.9); }
          100% { opacity: 1; transform: translateX(-50%) translateY(0) scale(1); }
        }

        @media (prefers-reduced-motion: reduce) {
          .box, .antenna-dot, .eye.blinking .sclera, .iris::before, .iris::after, .sparkle-tiny, .sparkle-1, .sparkle-2, .sparkle-3, .glow {
            animation: none !important;
          }
        }
      `}</style>
    </div>
  );
}

function Eye({ offsetX, offsetY, blinking }) {
  return (
    <div
      className={`eye${blinking ? ' blinking' : ''}`}
      style={{
        '--ox': `${offsetX}px`,
        '--oy': `${offsetY}px`,
      }}
    >
      <div className="glow" />
      <div className="sclera">
        <div className="iris">
          <div className="pupil" />
          <div className="sparkle-tiny" />
        </div>
        <div className="sparkle-1" />
        <div className="sparkle-2" />
        <div className="sparkle-3" />
      </div>
    </div>
  );
}
