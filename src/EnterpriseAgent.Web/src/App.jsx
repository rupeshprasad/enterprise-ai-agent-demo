import { useEffect, useRef, useState } from 'react'
import './App.css'

const currentTime = () => new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
const formatTime = (timestamp) => new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })

const createWelcomeMessage = () => ({
  role: 'assistant',
  text: 'Ask about customer verification, ordering eligibility, policy rules, or create a verification review request.',
  timestamp: currentTime(),
})

function App() {
  const [messages, setMessages] = useState(() => [createWelcomeMessage()])
  const [message, setMessage] = useState('')
  const [userId, setUserId] = useState('demo-user')
  const [isSending, setIsSending] = useState(false)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [isHistoryLoading, setIsHistoryLoading] = useState(true)
  const [refreshStatus, setRefreshStatus] = useState('')
  const conversationEndRef = useRef(null)

  useEffect(() => {
    conversationEndRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [messages, isSending])

  useEffect(() => {
    let ignoreResult = false

    async function loadHistory() {
      setIsHistoryLoading(true)
      try {
        const response = await fetch(`/api/chat/history?userId=${encodeURIComponent(userId)}`)
        const payload = await response.json()
        if (!response.ok) throw new Error(payload.answer || 'Unable to load chat history.')

        const history = payload.map((item) => ({
          ...item,
          userName: item.role === 'user' ? userId : undefined,
          timestamp: formatTime(item.timestamp),
          toolCalls: item.toolCalls || [],
          sources: item.sources || [],
        }))
        if (!ignoreResult) {
          setMessages(history.length > 0 ? history : [createWelcomeMessage()])
        }
      } catch (error) {
        if (!ignoreResult) {
          setMessages([
            createWelcomeMessage(),
            { role: 'error', text: error.message || 'Unable to load chat history.', timestamp: currentTime() },
          ])
        }
      } finally {
        if (!ignoreResult) setIsHistoryLoading(false)
      }
    }

    loadHistory()
    return () => {
      ignoreResult = true
    }
  }, [userId])

  async function refreshKnowledge() {
    setIsRefreshing(true)
    setRefreshStatus('Refreshing…')
    try {
      const response = await fetch('/api/knowledge/refresh', { method: 'POST' })
      const payload = await response.json()
      if (!response.ok) throw new Error(payload.message || 'Refresh failed.')
      setRefreshStatus(payload.message)
    } catch (error) {
      setRefreshStatus(error.message || 'Unable to refresh the knowledge base.')
    } finally {
      setIsRefreshing(false)
    }
  }

  async function clearConversation() {
    try {
      const response = await fetch(`/api/chat/history?userId=${encodeURIComponent(userId)}`, {
        method: 'DELETE',
      })
      if (!response.ok) throw new Error('Unable to clear chat history.')
      setMessages([createWelcomeMessage()])
      setRefreshStatus('')
    } catch (error) {
      setMessages((current) => [
        ...current,
        { role: 'error', text: error.message || 'Unable to clear chat history.', timestamp: currentTime() },
      ])
    }
  }

  async function sendMessage(event) {
    event.preventDefault()
    const trimmed = message.trim()
    if (!trimmed || isSending) return

    setMessages((current) => [
      ...current,
      { role: 'user', userName: userId, text: trimmed, timestamp: currentTime() },
    ])
    setMessage('')
    setIsSending(true)

    try {
      const response = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId, message: trimmed }),
      })
      const payload = await response.json()
      if (!response.ok) throw new Error(payload.answer || 'The request failed.')
      setMessages((current) => [
        ...current,
        {
          role: 'assistant',
          text: payload.answer,
          sources: payload.sources || [],
          toolCalls: payload.toolCalls || [],
          timestamp: currentTime(),
        },
      ])
    } catch (error) {
      setMessages((current) => [
        ...current,
        {
          role: 'error',
          text: error.message || 'Unable to reach the API. Confirm both projects are running.',
          timestamp: currentTime(),
        },
      ])
    } finally {
      setIsSending(false)
    }
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">EA</span>
          <div>
            <strong>Enterprise Agent</strong>
            <small>FDP demonstration</small>
          </div>
        </div>

        <button className="new-chat" type="button" onClick={clearConversation} disabled={isHistoryLoading || isSending}>
          <span>＋</span> New chat
        </button>

        <div className="capabilities">
          <span>Capabilities</span>
          <p>Policy knowledge</p>
          <p>Customer tools</p>
          <p>Secure actions</p>
        </div>

        <div className="sidebar-settings">
          <label htmlFor="demo-user">Demo user</label>
          <select id="demo-user" value={userId} onChange={(event) => setUserId(event.target.value)} disabled={isSending}>
            <option value="demo-user">demo-user</option>
            <option value="restricted-user">restricted-user</option>
          </select>
          <button className="refresh-button" type="button" onClick={refreshKnowledge} disabled={isRefreshing}>
            <span>↻</span> {isRefreshing ? 'Refreshing…' : 'Refresh knowledge'}
          </button>
          {refreshStatus && <small className="refresh-status">{refreshStatus}</small>}
        </div>
      </aside>

      <main className="chat-main">
        <header className="topbar">
          <div>
            <strong>Enterprise AI Agent</strong>
            <span>LLM · RAG · Tools · Actions · Security</span>
          </div>
          <span className="online"><i /> Demo environment</span>
        </header>

        <section className="chat" aria-live="polite">
          <div className="message-list">
            {messages.map((item, index) => (
              <article className={`message ${item.role}`} key={`${item.role}-${index}`}>
                <div className="avatar" aria-hidden="true">
                  {item.role === 'user' ? 'U' : item.role === 'error' ? '!' : 'AI'}
                </div>
                <div className="message-content">
                  <div className="message-heading">
                    <strong>{item.role === 'user' ? item.userName : item.role === 'error' ? 'Error' : 'Enterprise Agent'}</strong>
                    <time>{item.timestamp}</time>
                  </div>
                  <p>{item.text}</p>
                  {item.toolCalls?.length > 0 && (
                    <details className="agent-activity" open>
                      <summary>Agent activity · {item.toolCalls.length} tool{item.toolCalls.length === 1 ? '' : 's'}</summary>
                      {item.toolCalls.map((call, callIndex) => (
                        <div className="tool-call" key={`${call.tool}-${callIndex}`}>
                          <div className="tool-heading">
                            <strong>{call.tool}</strong>
                            <span className={`tool-status ${call.status.toLowerCase()}`}>{call.status}</span>
                          </div>
                          <details>
                            <summary>Input</summary>
                            <pre>{JSON.stringify(call.input, null, 2)}</pre>
                          </details>
                          {call.result && (
                            <details>
                              <summary>Result</summary>
                              <pre>{JSON.stringify(call.result, null, 2)}</pre>
                            </details>
                          )}
                        </div>
                      ))}
                    </details>
                  )}
                  {item.sources?.length > 0 && (
                    <div className="sources">
                      <span>Retrieved policy sources</span>
                      {item.sources.map((source) => (
                        <details key={`${source.file}-${source.section}`}>
                          <summary>{source.file} · {source.section}</summary>
                          <p>{source.snippet}</p>
                          <small>Similarity: {source.score.toFixed(4)}</small>
                        </details>
                      ))}
                    </div>
                  )}
                </div>
              </article>
            ))}
            {isSending && (
              <div className="thinking">
                <span /><span /><span />
                <small>Enterprise Agent is thinking</small>
              </div>
            )}
            {isHistoryLoading && <p className="history-status">Loading saved conversation…</p>}
            <div ref={conversationEndRef} aria-hidden="true" />
          </div>
        </section>

        <form className="chat-form" onSubmit={sendMessage}>
          <div className="composer">
            <textarea
              id="message"
              aria-label="Message"
              value={message}
              onChange={(event) => setMessage(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
                  event.preventDefault()
                  event.currentTarget.form?.requestSubmit()
                }
              }}
              placeholder="Message Enterprise Agent"
              rows="1"
            />
            <button type="submit" aria-label="Send message" disabled={isSending || !message.trim()}>
              ↑
            </button>
          </div>
          <small>Enter to send · Shift+Enter for a new line</small>
        </form>
      </main>
    </div>
  )
}

export default App
