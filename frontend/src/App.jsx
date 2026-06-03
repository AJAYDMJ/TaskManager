import { useEffect, useState } from 'react'
import './App.css'

const TOKEN_STORAGE_KEY = 'task-manager.jwt'

function App() {
  const [taskInput, setTaskInput] = useState('')
  const [tasks, setTasks] = useState([])
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const [token, setToken] = useState(() =>
    localStorage.getItem(TOKEN_STORAGE_KEY),
  )
  const [authMode, setAuthMode] = useState('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')

  const apiFetch = async (url, options = {}) => {
    const response = await fetch(url, {
      ...options,
      headers: {
        ...(options.headers ?? {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    })

    if (response.status === 401) {
      localStorage.removeItem(TOKEN_STORAGE_KEY)
      setToken(null)
      setTasks([])
      throw new Error('unauthorized')
    }

    return response
  }

  const loadTasks = async () => {
    if (!token) return

    setIsLoading(true)
    try {
      const response = await apiFetch('/api/tasks')
      if (!response.ok) {
        throw new Error('Failed to load tasks')
      }

      const data = await response.json()
      setTasks(data)
      setError('')
    } catch {
      setError('Could not load tasks from backend API.')
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => {
    if (token) {
      loadTasks()
    }
  }, [token])

  const handleAuthSubmit = async () => {
    const trimmedUsername = username.trim()
    if (!trimmedUsername || !password) {
      setError('Username and password are required.')
      return
    }

    setIsLoading(true)
    try {
      if (authMode === 'register') {
        const registerResponse = await fetch('/api/auth/register', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            username: trimmedUsername,
            password,
          }),
        })

        if (!registerResponse.ok) {
          const message = await registerResponse.text()
          throw new Error(
            message || `Register failed with status ${registerResponse.status}`,
          )
        }
      }

      const loginResponse = await fetch('/api/auth/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          username: trimmedUsername,
          password,
        }),
      })

      if (!loginResponse.ok) {
        const message = await loginResponse.text()
        throw new Error(
          message || `Login failed with status ${loginResponse.status}`,
        )
      }

      const { token: newToken } = await loginResponse.json()
      localStorage.setItem(TOKEN_STORAGE_KEY, newToken)
      setToken(newToken)
      setPassword('')
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed.')
    } finally {
      setIsLoading(false)
    }
  }

  const handleLogout = () => {
    localStorage.removeItem(TOKEN_STORAGE_KEY)
    setToken(null)
    setTasks([])
    setTaskInput('')
    setError('')
  }

  const handleAddTask = async () => {
    const trimmedTask = taskInput.trim()
    if (!trimmedTask) return

    try {
      const response = await apiFetch('/api/tasks', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          title: trimmedTask,
          isCompleted: false,
        }),
      })

      if (!response.ok) {
        throw new Error('Failed to create task')
      }

      const createdTask = await response.json()
      setTasks((previousTasks) => [createdTask, ...previousTasks])
      setTaskInput('')
      setError('')
    } catch {
      setError('Could not add task. Please check backend is running.')
    }
  }

  const handleDeleteTask = async (taskId) => {
    try {
      const response = await apiFetch(`/api/tasks/${taskId}`, {
        method: 'DELETE',
      })

      if (!response.ok) {
        throw new Error('Failed to delete task')
      }

      setTasks((previousTasks) =>
        previousTasks.filter((task) => task.id !== taskId),
      )
      setError('')
    } catch {
      setError('Could not delete task. Please try again.')
    }
  }

  const handleToggleTask = async (task) => {
    const updatedTask = {
      ...task,
      isCompleted: !task.isCompleted,
    }

    try {
      const response = await apiFetch(`/api/tasks/${task.id}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(updatedTask),
      })

      if (!response.ok) {
        throw new Error('Failed to update task')
      }

      setTasks((previousTasks) =>
        previousTasks.map((currentTask) =>
          currentTask.id === task.id ? updatedTask : currentTask,
        ),
      )
      setError('')
    } catch {
      setError('Could not update task status. Please try again.')
    }
  }

  return (
    <main className="task-manager">
      <h1>Task Manager</h1>

      {!token ? (
        <section className="auth-panel">
          <h2>{authMode === 'login' ? 'Login' : 'Register'}</h2>
          <div className="auth-controls">
            <input
              type="text"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              placeholder="Username"
              aria-label="Username"
            />
            <input
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              placeholder="Password"
              aria-label="Password"
            />
            <button type="button" onClick={handleAuthSubmit}>
              {authMode === 'login' ? 'Login' : 'Register'}
            </button>
          </div>
          <button
            type="button"
            className="text-button"
            onClick={() =>
              setAuthMode((currentMode) =>
                currentMode === 'login' ? 'register' : 'login',
              )
            }
          >
            {authMode === 'login'
              ? 'Need an account? Register'
              : 'Already have an account? Login'}
          </button>
          {error && <p className="error-message">{error}</p>}
        </section>
      ) : (
        <>
          <div className="toolbar">
            <span className="session-pill">Logged in</span>
            <button type="button" className="text-button" onClick={handleLogout}>
              Logout
            </button>
          </div>

      <div className="task-controls">
        <input
          type="text"
          value={taskInput}
          onChange={(event) => setTaskInput(event.target.value)}
          placeholder="Enter a task"
          aria-label="Task input"
        />
        <button type="button" onClick={handleAddTask}>
          Add Task
        </button>
      </div>
          {error && <p className="error-message">{error}</p>}

          {isLoading ? (
            <p className="loading-message">Loading tasks...</p>
          ) : (
            <ul className="task-list">
              {tasks.map((task) => (
                <li key={task.id}>
                  <label className="task-main">
                    <input
                      type="checkbox"
                      checked={task.isCompleted}
                      onChange={() => handleToggleTask(task)}
                      aria-label={`Mark ${task.title} as completed`}
                    />
                    <span className={task.isCompleted ? 'completed' : ''}>
                      {task.title}
                    </span>
                  </label>
                  <button type="button" onClick={() => handleDeleteTask(task.id)}>
                    Delete
                  </button>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </main>
  )
}

export default App
