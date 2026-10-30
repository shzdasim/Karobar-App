import React, { useEffect, useRef, useState } from "react";
import axios from "axios";
import { useLocation, useNavigate } from "react-router-dom";
import { ArrowTopRightOnSquareIcon, PaperAirplaneIcon, SparklesIcon, XMarkIcon } from "@heroicons/react/24/outline";
import "./AssistantWidget.css";

const createInitialConversations = () => ({
  nova: [{
    role: "assistant",
    content: "Hi! I’m Nova ✨ Ask me a general question, get help thinking something through, or ask me to write something.",
    suggestions: ["Help me plan my day", "Explain something simply", "How do I use Karobar-App?"],
  }],
  cube: [{
    role: "assistant",
    content: "Hey! I’m Cube 📦 I know the Karobar-App modules and can walk you through a task step by step.",
    suggestions: ["Show me how to add a product", "How do I make a sale?", "Explain the barcode POS"],
  }],
});

function MascotAvatar({ agent, small = false }) {
  const className = small ? "ka-avatar-svg ka-avatar-svg-small" : "ka-avatar-svg";
  if (agent === "cube") {
    return (
      <svg viewBox="0 0 64 64" className={className} aria-hidden="true">
        <rect x="7" y="8" width="50" height="48" rx="15" fill="#2563eb" />
        <path d="M32 8h10c8.3 0 15 6.7 15 15v18c0 8.3-6.7 15-15 15H32V8Z" fill="#1d4ed8" opacity=".58" />
        <path d="M14 18 32 10l18 8-18 9-18-9Z" fill="#60a5fa" opacity=".85" />
        <circle cx="23" cy="34" r="3" fill="#fff" />
        <circle cx="41" cy="34" r="3" fill="#fff" />
        <path d="M24 43q8 8 16 0" fill="none" stroke="#dbeafe" strokeWidth="3" strokeLinecap="round" />
        <circle cx="14" cy="42" r="3" fill="#fda4af" opacity=".9" />
        <circle cx="50" cy="42" r="3" fill="#fda4af" opacity=".9" />
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 64 64" className={className} aria-hidden="true">
      <circle cx="32" cy="32" r="29" fill="#7c3aed" />
      <path d="m32 5 6.5 17 18 2-14 11 4.5 18L32 43 17 53l4.5-18-14-11 18-2L32 5Z" fill="#fbbf24" />
      <circle cx="25" cy="31" r="2.7" fill="#312e81" />
      <circle cx="39" cy="31" r="2.7" fill="#312e81" />
      <path d="M25 39q7 7 14 0" fill="none" stroke="#312e81" strokeWidth="2.6" strokeLinecap="round" />
      <circle cx="18" cy="38" r="3.2" fill="#fda4af" opacity=".9" />
      <circle cx="46" cy="38" r="3.2" fill="#fda4af" opacity=".9" />
      <path d="m50 7 2.2 5.8L58 15l-5.8 2.2L50 23l-2.2-5.8L42 15l5.8-2.2L50 7Z" fill="#fde68a" />
    </svg>
  );
}

export default function AssistantWidget() {
  const navigate = useNavigate();
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const [agent, setAgent] = useState("nova");
  const [draft, setDraft] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [conversations, setConversations] = useState(createInitialConversations);
  const bottomRef = useRef(null);
  const activeMessages = conversations[agent] || [];

  useEffect(() => {
    if (open) bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [open, agent, activeMessages.length, loading]);

  useEffect(() => {
    if (!open) return undefined;
    const handleEscape = (event) => { if (event.key === "Escape") setOpen(false); };
    window.addEventListener("keydown", handleEscape);
    return () => window.removeEventListener("keydown", handleEscape);
  }, [open]);

  const sendMessage = async (value = draft) => {
    const content = String(value || "").trim();
    if (!content || loading) return;
    const requestedAgent = agent;
    const currentMessages = conversations[requestedAgent] || [];
    const nextMessages = [...currentMessages, { role: "user", content }];
    setConversations((previous) => ({ ...previous, [requestedAgent]: nextMessages }));
    setDraft("");
    setError("");
    setLoading(true);
    try {
      const { data } = await axios.post("/api/assistant/chat", {
        agent: requestedAgent,
        messages: nextMessages.slice(-10).map(({ role, content: messageContent }) => ({ role, content: messageContent })),
        page: location.pathname,
      });
      const assistantMessage = {
        role: "assistant",
        content: data?.reply || "I’m sorry, I couldn’t form a useful answer. Please try asking another way.",
        steps: Array.isArray(data?.steps) ? data.steps : [],
        suggestions: Array.isArray(data?.suggestions) ? data.suggestions : [],
        navigation: data?.navigation || null,
        handoff_to: data?.handoff_to || null,
      };
      setConversations((previous) => ({
        ...previous,
        [requestedAgent]: [...(previous[requestedAgent] || nextMessages), assistantMessage],
      }));
    } catch (requestError) {
      setError(requestError?.response?.data?.message || "I couldn’t reach the assistant service. Please try again in a moment.");
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = (event) => { event.preventDefault(); sendMessage(); };
  const handleInputKeyDown = (event) => {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      sendMessage();
    }
  };

  return (
    <div className="karobar-assistant-root">
      <button type="button" className="ka-launcher" onClick={() => setOpen((value) => !value)}
        aria-label={open ? "Close Nova and Cube assistant" : "Open Nova and Cube assistant"} aria-expanded={open}>
        <MascotAvatar agent="nova" />
        <span className="ka-launcher-sparkle"><SparklesIcon /></span>
      </button>

      {open && (
        <section className="ka-panel" role="dialog" aria-label="Nova and Cube AI assistant">
          <header className="ka-header">
            <div className="ka-header-identity">
              <div className="ka-header-avatar"><MascotAvatar agent={agent} /></div>
              <div className="ka-header-copy">
                <span className="ka-eyebrow">YOUR KAROBAR AI TEAM</span>
                <strong>{agent === "nova" ? "Nova" : "Cube"}</strong>
                <span>{agent === "nova" ? "Your friendly general assistant" : "Your Karobar software guide"}</span>
              </div>
            </div>
            <button type="button" className="ka-close" onClick={() => setOpen(false)} aria-label="Close assistant"><XMarkIcon /></button>
          </header>

          <div className="ka-agent-tabs" role="tablist" aria-label="Choose an assistant">
            <button type="button" role="tab" aria-selected={agent === "nova"}
              className={agent === "nova" ? "ka-agent-tab is-active" : "ka-agent-tab"}
              onClick={() => { setAgent("nova"); setError(""); }}>
              <MascotAvatar agent="nova" small />
              <span><strong>Nova</strong><small>Ask anything</small></span>
            </button>
            <button type="button" role="tab" aria-selected={agent === "cube"}
              className={agent === "cube" ? "ka-agent-tab is-active" : "ka-agent-tab"}
              onClick={() => { setAgent("cube"); setError(""); }}>
              <MascotAvatar agent="cube" small />
              <span><strong>Cube</strong><small>Learn the software</small></span>
            </button>
          </div>

          <div className="ka-thread" aria-live="polite">
            <div className="ka-thread-intro">
              {agent === "nova" ? "A little help, a big idea, or a tricky question?" : "Tell me what you want to do in Karobar-App."}
            </div>
            {activeMessages.map((message, index) => (
              <div key={agent + "-" + index} className={message.role === "user" ? "ka-message-row is-user" : "ka-message-row"}>
                {message.role !== "user" && <div className="ka-message-avatar"><MascotAvatar agent={agent} small /></div>}
                <div className="ka-message-content">
                  <div className="ka-message-bubble">
                    <div className="ka-message-text">{message.content}</div>
                    {message.steps?.length > 0 && (
                      <div className="ka-steps">
                        <div className="ka-steps-heading">Let’s do it together</div>
                        {message.steps.map((step, stepIndex) => (
                          <div className="ka-step" key={stepIndex}>
                            <span className="ka-step-number">{stepIndex + 1}</span>
                            <div><strong>{step.title}</strong><p>{step.description}</p></div>
                          </div>
                        ))}
                      </div>
                    )}
                    {message.navigation?.path && (
                      <button type="button" className="ka-navigation-action" onClick={() => navigate(message.navigation.path)}>
                        Open {message.navigation.label || "module"} <ArrowTopRightOnSquareIcon />
                      </button>
                    )}
                    {message.handoff_to && (
                      <button type="button" className="ka-handoff-action"
                        onClick={() => { setAgent(message.handoff_to); setError(""); }}>
                        Continue with {message.handoff_to === "cube" ? "Cube" : "Nova"} <span>→</span>
                      </button>
                    )}
                  </div>
                  {message.suggestions?.length > 0 && (
                    <div className="ka-suggestions">
                      {message.suggestions.map((suggestion, suggestionIndex) => (
                        <button type="button" className="ka-suggestion" key={suggestionIndex}
                          disabled={loading} onClick={() => sendMessage(suggestion)}>{suggestion}</button>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            ))}
            {loading && (
              <div className="ka-message-row">
                <div className="ka-message-avatar"><MascotAvatar agent={agent} small /></div>
                <div className="ka-thinking" aria-label={agent === "nova" ? "Nova is thinking" : "Cube is thinking"}>
                  <span /><span /><span /><small>{agent === "nova" ? "Nova is thinking" : "Cube is thinking"}</small>
                </div>
              </div>
            )}
            <div ref={bottomRef} />
          </div>

          {error && <div className="ka-error" role="alert">{error}</div>}
          <form className="ka-composer" onSubmit={handleSubmit}>
            <textarea className="ka-input" value={draft} onChange={(event) => setDraft(event.target.value)}
              onKeyDown={handleInputKeyDown} placeholder={agent === "nova" ? "Ask Nova anything…" : "Ask Cube how to do something…"}
              rows={1} maxLength={4000} aria-label="Message" />
            <button type="submit" className="ka-send" disabled={!draft.trim() || loading} aria-label="Send message"><PaperAirplaneIcon /></button>
          </form>
          <div className="ka-footer">AI can make mistakes. Verify important business figures in your ERP records.</div>
        </section>
      )}
    </div>
  );
}
