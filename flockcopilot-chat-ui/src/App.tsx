import React, { useState, useRef, useEffect } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import './App.css';

interface Message {
  role: 'user' | 'assistant' | 'system';
  content: string;
  sources?: Array<{ title: string; url: string }>;
  timestamp: Date;
}

interface BestPracticesDocument {
  title?: string;
  source?: string;
  downloadUrl?: string;
}

interface ChatApiResponse {
  answer?: string;
  anomalies?: string[];
  staleFlocks?: string[];
  bestPracticesDocuments?: BestPracticesDocument[];
}

function App() {
  const [messages, setMessages] = useState<Message[]>([
    {
      role: 'system',
      content: 'Flocky Chat ready. Ask about live flock performance, anomalies, trends, or best-practices remediation.',
      timestamp: new Date()
    }
  ]);
  const [input, setInput] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [apiUrl, setApiUrl] = useState(localStorage.getItem('apiUrl') || 'http://localhost:8080');
  const [tenantIdClaim, setTenantIdClaim] = useState(localStorage.getItem('tenantIdClaim') || 'tenant-demo-123');
  const [availableTenants, setAvailableTenants] = useState<string[]>([]);
  const [useCustomTenant, setUseCustomTenant] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const useMarkdown = new URLSearchParams(window.location.search).get('use-markdown') === 'true';

  useEffect(() => {
    const el = messagesEndRef.current;
    if (el && typeof (el as any).scrollIntoView === 'function') {
      (el as any).scrollIntoView({ behavior: 'smooth' });
    }
  }, [messages]);

  useEffect(() => {
    const loadTenants = async () => {
      try {
        const response = await fetch(`${apiUrl}/api/tenants`);
        if (!response.ok) return;
        const data = await response.json();
        const tenants = (data.tenants || []) as string[];
        setAvailableTenants(tenants);
      } catch {
        // ignore
      }
    };

    loadTenants();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiUrl]);

  useEffect(() => {
    localStorage.setItem('tenantIdClaim', tenantIdClaim);
  }, [tenantIdClaim]);

  const saveSettings = () => {
    localStorage.setItem('apiUrl', apiUrl);
    localStorage.setItem('tenantIdClaim', tenantIdClaim);
    setShowSettings(false);
  };

  const tenantHeaders: Record<string, string> = tenantIdClaim.trim()
    ? { 'X-Tenant-Id-Claim': tenantIdClaim.trim() }
    : {};

  const callChatApi = async (message: string): Promise<ChatApiResponse> => {
    const base = apiUrl.replace(/\/$/, '');
    const url = `${base}/api/chat${useMarkdown ? '?useMarkdown=true' : ''}`;
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...tenantHeaders
      },
      body: JSON.stringify({
        message,
        mode: 'auto',
        clientNowUtc: new Date().toISOString(),
        tzOffsetMinutes: new Date().getTimezoneOffset()
      })
    });

    if (!response.ok) {
      const body = await response.text();
      throw new Error(body || `Chat API error: ${response.status}`);
    }

    return (await response.json()) as ChatApiResponse;
  };

  const handleSend = async () => {
    if (!input.trim() || isLoading) return;

    const userMessage: Message = {
      role: 'user',
      content: input,
      timestamp: new Date()
    };

    setMessages(prev => [...prev, userMessage]);
    setInput('');
    setIsLoading(true);

    try {
      const data = await callChatApi(input);
      let assistantText = (data.answer || '').trim();

      const anomalies = (data.anomalies || []).filter(Boolean);
      if (anomalies.length) {
        assistantText = `Detected anomalies:\n- ${anomalies.join('\n- ')}\n\n${assistantText}`;
      }

      const docs = data.bestPracticesDocuments || [];
      const sources =
        docs.length
          ? docs
              .map((d) => {
                const title = d.title || 'Untitled';
                const href = d.downloadUrl
                  ? d.downloadUrl.startsWith('http')
                    ? d.downloadUrl
                    : `${apiUrl.replace(/\/$/, '')}${d.downloadUrl.startsWith('/') ? '' : '/'}${d.downloadUrl}`
                  : d.source
                    ? d.source.startsWith('http')
                      ? d.source
                      : `${apiUrl.replace(/\/$/, '')}/${d.source.replace(/^\//, '')}`
                    : '';
                return href ? { title, url: href } : null;
              })
              .filter((x): x is { title: string; url: string } => Boolean(x))
          : [];

      const assistantMessage: Message = {
        role: 'assistant',
        content: assistantText || 'No response content returned.',
        sources: sources.length ? sources : undefined,
        timestamp: new Date()
      };

      setMessages(prev => [...prev, assistantMessage]);
    } catch (error) {
      const errorMessage: Message = {
        role: 'assistant',
        content: `Error: ${error instanceof Error ? error.message : 'Unknown error occurred'}. Please check your API settings.`,
        timestamp: new Date()
      };
      setMessages(prev => [...prev, errorMessage]);
    } finally {
      setIsLoading(false);
    }
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const quickQuestions = [
    "Are there any anomalies right now?",
    "Have there been any anomalies today?",
    "What's happening with building-a?",
    'What are best practices around temperature control?',
    'What ventilation steps help with high CO₂ readings?'
  ];

  return (
    <div className="App">
      <header className="App-header">
        <div className="header-left">
          <div className="logo-chicken"></div>
          <h1>Flocky Chat</h1>
        </div>
        <div className="tenant-controls">
          <span className="tenant-label">Tenant</span>
          {useCustomTenant ? (
            <input
              type="text"
              className="tenant-input"
              value={tenantIdClaim}
              onChange={(e) => setTenantIdClaim(e.target.value)}
              placeholder="tenant-demo-123"
              disabled={isLoading}
            />
          ) : (
            <select
              className="tenant-select"
              value={tenantIdClaim}
              onChange={(e) => setTenantIdClaim(e.target.value)}
              disabled={isLoading || availableTenants.length === 0}
              title={availableTenants.length === 0 ? 'No tenants found yet (ingest data first).' : ''}
            >
              {(availableTenants.length ? availableTenants : ['tenant-demo-123']).map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          )}
          <button
            className="tenant-toggle-btn"
            onClick={() => setUseCustomTenant((v) => !v)}
            disabled={isLoading}
            title={useCustomTenant ? 'Use tenant list' : 'Use custom tenant'}
          >
            {useCustomTenant ? 'List' : 'Custom'}
          </button>
          <button className="settings-btn" onClick={() => setShowSettings(!showSettings)}>
            ⚙️ Settings
          </button>
        </div>
      </header>

	      {showSettings && (
	        <div className="settings-panel">
	          <h3>Configuration</h3>
	          <div className="setting-group">
	            <label>FlockCopilot API URL:</label>
	            <input
	              type="text"
	              value={apiUrl}
	              onChange={(e) => setApiUrl(e.target.value)}
	              placeholder="http://localhost:8080"
	            />
	          </div>
	          <p className="setting-note">Azure OpenAI + Azure AI Search are called server-side via `POST /api/chat`.</p>
	          <button onClick={saveSettings} className="save-btn">Save Settings</button>
	        </div>
      )}

      <div className="chat-container">
        <p className="mode-hint">
          Auto mode: the API uses telemetry for diagnostics, and pulls best-practices docs for remediation or how-to questions.
        </p>
        {useMarkdown && (
          <p className="mode-hint">
            Markdown rendering enabled (`?use-markdown=true`).
          </p>
        )}
        <div className="messages">
          {messages.map((msg, idx) => (
            <div key={idx} className={`message ${msg.role}`}>
              <div className="message-header">
                <span className="role">{msg.role === 'user' ? '👤 You' : msg.role === 'assistant' ? '🤖 Agent' : 'ℹ️ System'}</span>
                <span className="timestamp">{msg.timestamp.toLocaleTimeString()}</span>
              </div>
              <div className="message-content">
                <div className="message-text">
                  {useMarkdown && msg.role === 'assistant' ? (
                    <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown>
                  ) : (
                    msg.content
                  )}
                </div>
                {msg.sources && msg.sources.length > 0 && (
                  <div className="message-sources">
                    <div className="sources-title">Sources</div>
                    <ul>
                      {msg.sources.map((s) => (
                        <li key={s.url}>
                          <a href={s.url} target="_blank" rel="noreferrer noopener">
                            {s.title}
                          </a>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            </div>
          ))}
          {isLoading && (
            <div className="message assistant">
              <div className="message-header">
                <span className="role">🤖 Agent</span>
              </div>
              <div className="message-content">
                <div className="typing-indicator">
                  <span></span><span></span><span></span>
                </div>
              </div>
            </div>
          )}
          <div ref={messagesEndRef} />
        </div>

        <div className="quick-questions">
          {quickQuestions.map((q, idx) => (
            <button
              key={idx}
              onClick={() => setInput(q)}
              className="quick-btn"
              disabled={isLoading}
            >
              {q}
            </button>
          ))}
        </div>

        <div className="input-container">
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyPress={handleKeyPress}
            placeholder="Ask about building performance, trends, or anomalies..."
            disabled={isLoading}
            rows={2}
          />
          <button onClick={handleSend} disabled={isLoading || !input.trim()}>
            Send
          </button>
        </div>
      </div>

      <footer className="App-footer">
        <span>Multi-Sensor Zone-Based IoT Monitoring</span>
        <span>|</span>
        <span>3 Buildings · 18 Sensors · Real-time Diagnostics</span>
      </footer>
    </div>
  );
}

export default App;
