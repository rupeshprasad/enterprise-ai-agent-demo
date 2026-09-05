import { useEffect, useRef, useState } from 'react'
import './App.css'

const currentTime = () => new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
const formatTime = (timestamp) => new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })

const demoModes = [
  { value: 'LlmOnly', label: 'LLM Only', rag: false, readTools: false, actions: false },
  { value: 'Rag', label: 'LLM + RAG', rag: true, readTools: false, actions: false },
  { value: 'RagAndTools', label: 'LLM + RAG + Tools', rag: true, readTools: true, actions: false },
  { value: 'FullAgent', label: 'Full Agent', rag: true, readTools: true, actions: true },
]
const emptyCustomer = { customerId: '', name: '', country: 'US', verificationStatus: 'Pending' }

const createWelcomeMessage = () => ({
  role: 'assistant',
  text: 'Ask about customer verification, ordering eligibility, policy rules, or create a verification review request.',
  timestamp: currentTime(),
})

function App() {
  const [messages, setMessages] = useState(() => [createWelcomeMessage()])
  const [message, setMessage] = useState('')
  const [userId, setUserId] = useState('demo-user')
  const [demoMode, setDemoMode] = useState(() => sessionStorage.getItem('demoCapabilityMode') || 'FullAgent')
  const [isSending, setIsSending] = useState(false)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [isHistoryLoading, setIsHistoryLoading] = useState(true)
  const [refreshStatus, setRefreshStatus] = useState('')
  const [isAccessOpen, setIsAccessOpen] = useState(false)
  const [isAccessLoading, setIsAccessLoading] = useState(false)
  const [accessOptions, setAccessOptions] = useState([])
  const [accessStatus, setAccessStatus] = useState('')
  const [isCustomerOpen, setIsCustomerOpen] = useState(false)
  const [isCustomerLoading, setIsCustomerLoading] = useState(false)
  const [customerRecords, setCustomerRecords] = useState([])
  const [editingCustomerId, setEditingCustomerId] = useState(null)
  const [isCustomerFormOpen, setIsCustomerFormOpen] = useState(false)
  const [customerStatus, setCustomerStatus] = useState('')
  const [newCustomer, setNewCustomer] = useState(emptyCustomer)
  const conversationEndRef = useRef(null)
  const activeMode = demoModes.find((mode) => mode.value === demoMode) || demoModes[3]

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
          activity: item.activity || [],
          demoMode: item.demoMode || 'FullAgent',
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

  function changeDemoMode(nextMode) {
    const selected = demoModes.find((mode) => mode.value === nextMode) || demoModes[3]
    setDemoMode(selected.value)
    sessionStorage.setItem('demoCapabilityMode', selected.value)
    setMessages((current) => [
      ...current,
      { role: 'mode-change', text: `Demo mode changed: ${selected.label}`, timestamp: currentTime() },
    ])
  }

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

  async function loadAccess() {
    setIsAccessOpen(true)
    setIsAccessLoading(true)
    setAccessStatus('')
    try {
      const response = await fetch(`/api/access?userId=${encodeURIComponent(userId)}`)
      const payload = await response.json()
      if (!response.ok) throw new Error(payload.message || 'Unable to load customer access.')
      setAccessOptions(payload)
    } catch (error) {
      setAccessStatus(error.message || 'Unable to load customer access.')
    } finally {
      setIsAccessLoading(false)
    }
  }

  async function updateAccess(customerId, hasAccess) {
    setIsAccessLoading(true)
    setAccessStatus('Saving…')
    try {
      const response = await fetch('/api/access', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId, customerId, hasAccess }),
      })
      const payload = await response.json()
      if (!response.ok) throw new Error(payload.message || 'Unable to update customer access.')
      setAccessOptions(payload)
      setAccessStatus(`Access updated for ${userId}.`)
    } catch (error) {
      setAccessStatus(error.message || 'Unable to update customer access.')
    } finally {
      setIsAccessLoading(false)
    }
  }

  async function removeAllAccess() {
    const assigned = accessOptions.filter((customer) => customer.hasAccess)
    if (assigned.length === 0) {
      setAccessStatus(`${userId} already has no customer access.`)
      return
    }

    setIsAccessLoading(true)
    setAccessStatus('Removing access…')
    try {
      for (const customer of assigned) {
        const response = await fetch('/api/access', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ userId, customerId: customer.customerId, hasAccess: false }),
        })
        if (!response.ok) throw new Error('Unable to remove all customer access.')
      }
      setAccessOptions((current) => current.map((customer) => ({ ...customer, hasAccess: false })))
      setAccessStatus(`${userId} now has no customer access.`)
    } catch (error) {
      setAccessStatus(error.message || 'Unable to remove all customer access.')
    } finally {
      setIsAccessLoading(false)
    }
  }

  async function loadCustomers() {
    setIsCustomerOpen(true)
    setIsCustomerLoading(true)
    setCustomerStatus('')
    setEditingCustomerId(null)
    setIsCustomerFormOpen(false)
    setNewCustomer(emptyCustomer)
    try {
      const response = await fetch('/api/access/customers')
      const payload = await response.json()
      if (!response.ok) throw new Error(payload.message || 'Unable to load customers.')
      setCustomerRecords(payload)
    } catch (error) {
      setCustomerStatus(error.message || 'Unable to load customers.')
    } finally {
      setIsCustomerLoading(false)
    }
  }

  function editCustomer(customer) {
    setEditingCustomerId(customer.customerId)
    setIsCustomerFormOpen(true)
    setNewCustomer({
      customerId: customer.customerId,
      name: customer.name,
      country: customer.country,
      verificationStatus: customer.verificationStatus,
    })
    setCustomerStatus('')
  }

  async function saveCustomer(event) {
    event.preventDefault()
    setIsCustomerLoading(true)
    setCustomerStatus(editingCustomerId ? 'Saving customer…' : 'Adding customer…')
    try {
      const url = editingCustomerId
        ? `/api/access/customers/${encodeURIComponent(editingCustomerId)}`
        : '/api/access/customers'
      const response = await fetch(url, {
        method: editingCustomerId ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(newCustomer),
      })
      const payload = await response.json()
      if (!response.ok) throw new Error(payload.message || 'Unable to save customer.')
      const listResponse = await fetch('/api/access/customers')
      const listPayload = await listResponse.json()
      if (!listResponse.ok) throw new Error('Customer saved, but the list could not be refreshed.')
      setCustomerRecords(listPayload)
      setNewCustomer(emptyCustomer)
      setEditingCustomerId(null)
      setIsCustomerFormOpen(false)
      setCustomerStatus(`${payload.customerId} was saved successfully.`)
    } catch (error) {
      setCustomerStatus(error.message || 'Unable to save customer.')
    } finally {
      setIsCustomerLoading(false)
    }
  }

  async function deleteCustomer(customer) {
    if (!window.confirm(`Delete ${customer.customerId} (${customer.name})? This also removes verification and access assignments.`)) return
    setIsCustomerLoading(true)
    setCustomerStatus(`Deleting ${customer.customerId}…`)
    try {
      const response = await fetch(`/api/access/customers/${encodeURIComponent(customer.customerId)}`, { method: 'DELETE' })
      if (!response.ok) {
        const payload = await response.json()
        throw new Error(payload.message || 'Unable to delete customer.')
      }
      setCustomerRecords((current) => current.filter((item) => item.customerId !== customer.customerId))
      if (editingCustomerId === customer.customerId) {
        setEditingCustomerId(null)
        setIsCustomerFormOpen(false)
        setNewCustomer(emptyCustomer)
      }
      setCustomerStatus(`${customer.customerId} was deleted.`)
    } catch (error) {
      setCustomerStatus(error.message || 'Unable to delete customer.')
    } finally {
      setIsCustomerLoading(false)
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
        body: JSON.stringify({ userId, message: trimmed, demoMode }),
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
          activity: payload.activity || [],
          demoMode: payload.demoMode || demoMode,
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

        <div className="mode-panel">
          <span className="panel-label">Demo Capability Mode</span>
          <div className="mode-options" role="radiogroup" aria-label="Demo Capability Mode">
            {demoModes.map((mode) => (
              <label className={demoMode === mode.value ? 'selected' : ''} key={mode.value}>
                <input
                  type="radio"
                  name="demo-mode"
                  value={mode.value}
                  checked={demoMode === mode.value}
                  onChange={() => changeDemoMode(mode.value)}
                  disabled={isSending}
                />
                <span>{mode.label}</span>
              </label>
            ))}
          </div>

          <div className="capability-status">
            <span className="panel-label">Current Capabilities</span>
            {[
              ['LLM', true],
              ['RAG', activeMode.rag],
              ['Read Tools', activeMode.readTools],
              ['Actions', activeMode.actions],
              ['Authorization', true],
            ].map(([label, enabled]) => (
              <div key={label}><span>{label}</span><b className={enabled ? 'enabled' : 'disabled'}>{enabled ? '✓' : '✕'}</b></div>
            ))}
          </div>
        </div>

        <div className="sidebar-settings">
          <label htmlFor="demo-user">Demo user</label>
          <select id="demo-user" value={userId} onChange={(event) => { setUserId(event.target.value); setIsAccessOpen(false) }} disabled={isSending}>
            <option value="demo-user">demo-user</option>
            <option value="restricted-user">restricted-user</option>
          </select>
          <button className="refresh-button" type="button" onClick={loadAccess} disabled={isSending}>
            <span>⚿</span> Manage access
          </button>
          <button className="refresh-button" type="button" onClick={loadCustomers} disabled={isSending}>
            <span>☷</span> Manage customers
          </button>
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
            <span>{activeMode.label} · Authorization always enforced</span>
          </div>
          <span className="online"><i /> Demo environment</span>
        </header>

        <section className="chat" aria-live="polite">
          <div className="message-list">
            {messages.map((item, index) => (
              item.role === 'mode-change' ? (
                <div className="mode-divider" key={`mode-${index}`}><span>{item.text}</span></div>
              ) : (
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
                  {item.activity?.length > 0 && (
                    <details className="agent-activity" open>
                      <summary>Activity trace · {item.demoMode || 'Full Agent'}</summary>
                      <ul className="activity-list">
                        {item.activity.map((entry, activityIndex) => <li key={`${entry}-${activityIndex}`}>{entry}</li>)}
                      </ul>
                    </details>
                  )}
                  {item.toolCalls?.length > 0 && (
                    <details className="agent-activity">
                      <summary>Tool details · {item.toolCalls.length} tool{item.toolCalls.length === 1 ? '' : 's'}</summary>
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
              )
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

      {isAccessOpen && (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setIsAccessOpen(false)}>
          <section className="access-modal" role="dialog" aria-modal="true" aria-labelledby="access-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="modal-heading">
              <div>
                <span>Demo authorization</span>
                <h2 id="access-title">Customer access for {userId}</h2>
              </div>
              <button type="button" aria-label="Close access dialog" onClick={() => setIsAccessOpen(false)}>×</button>
            </div>
            <p>Changes take effect on the next agent request—no API restart is required.</p>
            <div className="access-list">
              {accessOptions.map((customer) => (
                <label key={customer.customerId}>
                  <span><strong>{customer.customerId}</strong><small>{customer.customerName}</small></span>
                  <input
                    type="checkbox"
                    checked={customer.hasAccess}
                    onChange={(event) => updateAccess(customer.customerId, event.target.checked)}
                    disabled={isAccessLoading}
                  />
                </label>
              ))}
              {isAccessLoading && accessOptions.length === 0 && <small>Loading customer directory…</small>}
            </div>
            {accessStatus && <p className="access-status">{accessStatus}</p>}
            <div className="modal-actions">
              <button type="button" className="remove-access" onClick={removeAllAccess} disabled={isAccessLoading}>Remove all access</button>
              <button type="button" className="done-button" onClick={() => setIsAccessOpen(false)}>Done</button>
            </div>
          </section>
        </div>
      )}

      {isCustomerOpen && (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setIsCustomerOpen(false)}>
          <section className="customer-modal" role="dialog" aria-modal="true" aria-labelledby="customer-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="modal-heading">
              <div>
                <span>Customer master</span>
                <h2 id="customer-title">Manage customers</h2>
              </div>
              <button type="button" aria-label="Close customer dialog" onClick={() => setIsCustomerOpen(false)}>×</button>
            </div>
            <p>Create, review, edit, or delete customer master and verification information. Access assignments remain separate.</p>
            {!isCustomerFormOpen ? (
              <>
                <div className="customer-toolbar">
                  <button type="button" onClick={() => { setEditingCustomerId(null); setNewCustomer(emptyCustomer); setCustomerStatus(''); setIsCustomerFormOpen(true) }}>＋ Add customer</button>
                </div>
                <div className="customer-table-wrap">
                  <table className="customer-table">
                    <thead><tr><th>ID</th><th>Name</th><th>Country</th><th>Verification</th><th aria-label="Actions" /></tr></thead>
                    <tbody>
                      {customerRecords.map((customer) => (
                        <tr key={customer.customerId}>
                          <td><strong>{customer.customerId}</strong></td>
                          <td>{customer.name}</td>
                          <td>{customer.country}</td>
                          <td>{customer.verificationStatus}</td>
                          <td className="row-actions">
                            <button type="button" className="icon-button" title={`Edit ${customer.customerId}`} aria-label={`Edit ${customer.customerId}`} onClick={() => editCustomer(customer)} disabled={isCustomerLoading}>✎</button>
                            <button type="button" className="icon-button delete-customer" title={`Delete ${customer.customerId}`} aria-label={`Delete ${customer.customerId}`} onClick={() => deleteCustomer(customer)} disabled={isCustomerLoading}>🗑</button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {isCustomerLoading && customerRecords.length === 0 && <small>Loading customer master…</small>}
                </div>
              </>
            ) : (
              <>
                <div className="customer-form-heading">
                  <strong>{editingCustomerId ? `Edit ${editingCustomerId}` : 'Add new customer'}</strong>
                  <button type="button" onClick={() => { setEditingCustomerId(null); setNewCustomer(emptyCustomer); setCustomerStatus(''); setIsCustomerFormOpen(false) }}>← Back to customer list</button>
                </div>
                <form className="add-customer-form" onSubmit={saveCustomer}>
                  <label>Customer ID<input value={newCustomer.customerId} onChange={(event) => setNewCustomer({ ...newCustomer, customerId: event.target.value })} placeholder="GHI012" required disabled={Boolean(editingCustomerId)} /></label>
                  <label>Customer name<input value={newCustomer.name} onChange={(event) => setNewCustomer({ ...newCustomer, name: event.target.value })} placeholder="Example Industries" required /></label>
                  <label>Country<input value={newCustomer.country} onChange={(event) => setNewCustomer({ ...newCustomer, country: event.target.value })} placeholder="US" required /></label>
                  <label>Verification<select value={newCustomer.verificationStatus} onChange={(event) => setNewCustomer({ ...newCustomer, verificationStatus: event.target.value })}><option>Pending</option><option>Approved</option><option>Rejected</option></select></label>
                  <button type="submit" disabled={isCustomerLoading}>{editingCustomerId ? 'Save changes' : 'Add customer'}</button>
                </form>
              </>
            )}
            {customerStatus && <p className="access-status">{customerStatus}</p>}
            <div className="modal-actions">
              <button type="button" className="done-button" onClick={() => setIsCustomerOpen(false)}>Done</button>
            </div>
          </section>
        </div>
      )}
    </div>
  )
}

export default App
