// VeloxLink — Technician Mobile Companion App
(function() {
  'use strict';

  // State
  let state = {
    token: localStorage.getItem('vx_token') || '',
    user: JSON.parse(localStorage.getItem('vx_user') || 'null'),
    apiKey: localStorage.getItem('vx_api_key') || 'vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf',
    jobs: [],
    selectedJob: null,
    selectedStatus: null,
    pollTimer: null
  };

  // DOM Elements
  const el = {
    loginView: document.getElementById('login-view'),
    jobsView: document.getElementById('jobs-view'),
    loginForm: document.getElementById('login-form'),
    empName: document.getElementById('emp-name'),
    empPin: document.getElementById('emp-pin'),
    apiKeyInput: document.getElementById('api-key-input'),
    loginError: document.getElementById('login-error'),
    userPill: document.getElementById('user-pill'),
    userNameDisplay: document.getElementById('user-name-display'),
    logoutBtn: document.getElementById('logout-btn'),
    jobsList: document.getElementById('jobs-list'),
    queueSubtitle: document.getElementById('queue-subtitle'),
    emptyState: document.getElementById('empty-state'),
    jobSearch: document.getElementById('job-search'),
    btnRefresh: document.getElementById('btn-refresh'),
    modal: document.getElementById('action-modal'),
    modalTitle: document.getElementById('modal-title'),
    modalSubtitle: document.getElementById('modal-subtitle'),
    modalNotes: document.getElementById('modal-notes'),
    modalClose: document.getElementById('modal-close'),
    modalCancel: document.getElementById('modal-cancel'),
    modalSave: document.getElementById('modal-save'),
    statusBtns: document.querySelectorAll('.btn-status')
  };

  // ── Initialization ──────────────────────────────────────────────────────────
  function init() {
    if (state.apiKey) el.apiKeyInput.value = state.apiKey;

    if (state.token && state.user) {
      showJobsView();
    } else {
      showLoginView();
    }

    // Event Listeners
    el.loginForm.addEventListener('submit', handleLogin);
    el.logoutBtn.addEventListener('click', handleLogout);
    el.btnRefresh.addEventListener('click', () => fetchJobs(true));
    el.jobSearch.addEventListener('input', renderJobs);

    // Modal
    el.modalClose.addEventListener('click', closeModal);
    el.modalCancel.addEventListener('click', closeModal);
    el.modalSave.addEventListener('click', handleSaveStatus);

    el.statusBtns.forEach(btn => {
      btn.addEventListener('click', (e) => {
        el.statusBtns.forEach(b => b.classList.remove('selected'));
        btn.classList.add('selected');
        state.selectedStatus = btn.dataset.status;
      });
    });
  }

  // ── Authentication ──────────────────────────────────────────────────────────
  async function handleLogin(e) {
    e.preventDefault();
    el.loginError.classList.add('hidden');

    const name = el.empName.value.trim();
    const pin = el.empPin.value.trim();
    const apiKey = el.apiKeyInput.value.trim();

    if (!name || !pin) return;

    try {
      const res = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-API-Key': apiKey
        },
        body: JSON.stringify({ name, pin })
      });

      const data = await res.json();
      if (!res.ok) {
        throw new Error(data.error || 'Authentication failed.');
      }

      state.token = data.token;
      state.user = { id: data.employeeId, name: data.name, role: data.role };
      state.apiKey = apiKey;

      localStorage.setItem('vx_token', state.token);
      localStorage.setItem('vx_user', JSON.stringify(state.user));
      localStorage.setItem('vx_api_key', state.apiKey);

      showJobsView();
    } catch (err) {
      el.loginError.textContent = err.message;
      el.loginError.classList.remove('hidden');
    }
  }

  function handleLogout() {
    state.token = '';
    state.user = null;
    localStorage.removeItem('vx_token');
    localStorage.removeItem('vx_user');
    if (state.pollTimer) clearInterval(state.pollTimer);
    showLoginView();
  }

  // ── View Transitions ────────────────────────────────────────────────────────
  function showLoginView() {
    el.loginView.classList.remove('hidden');
    el.jobsView.classList.add('hidden');
    el.userPill.classList.add('hidden');
  }

  function showJobsView() {
    el.loginView.classList.add('hidden');
    el.jobsView.classList.remove('hidden');
    el.userPill.classList.remove('hidden');
    el.userNameDisplay.textContent = state.user?.name || 'Technician';

    fetchJobs(true);

    // Auto-refresh poll every 4 seconds
    if (state.pollTimer) clearInterval(state.pollTimer);
    state.pollTimer = setInterval(() => fetchJobs(false), 4000);
  }

  // ── Job Fetching & Rendering ────────────────────────────────────────────────
  async function fetchJobs(showLoading) {
    if (showLoading) {
      el.queueSubtitle.textContent = 'Refreshing active queue...';
    }

    try {
      const res = await fetch('/api/v1/jobs/mine', {
        headers: {
          'Authorization': `Bearer ${state.token}`
        }
      });

      if (res.status === 401) {
        handleLogout();
        return;
      }

      if (!res.ok) throw new Error('Failed to load jobs');

      const data = await res.json();
      state.jobs = data.jobs || [];
      renderJobs();
      el.queueSubtitle.textContent = `${state.jobs.length} assigned unit${state.jobs.length === 1 ? '' : 's'}`;
    } catch (err) {
      el.queueSubtitle.textContent = 'Sync error. Retrying...';
    }
  }

  function renderJobs() {
    const q = el.jobSearch.value.trim().toLowerCase();
    const filtered = state.jobs.filter(j => {
      if (!q) return true;
      return (j.serialNumber || '').toLowerCase().includes(q) ||
             (j.assetTag || '').toLowerCase().includes(q) ||
             (j.model || '').toLowerCase().includes(q);
    });

    if (filtered.length === 0) {
      el.jobsList.innerHTML = '';
      el.emptyState.classList.remove('hidden');
      return;
    }

    el.emptyState.classList.add('hidden');
    el.jobsList.innerHTML = filtered.map(job => {
      const badgeClass = getBadgeClass(job.queue);
      const queueName = getQueueName(job.queue);

      return `
        <div class="job-card" data-id="${job.assetId}">
          <div class="job-header">
            <div>
              <div class="job-serial">${escapeHtml(job.serialNumber || 'SN-UNKNOWN')}</div>
              <div class="job-tag">Tag: ${escapeHtml(job.assetTag || 'TAG-PENDING')}</div>
            </div>
            <span class="badge ${badgeClass}">${queueName}</span>
          </div>
          <div class="job-model">${escapeHtml(job.model || 'Chassis Model')}</div>
          ${job.notes ? `<div class="job-notes">📝 ${escapeHtml(job.notes)}</div>` : ''}
          <div class="job-actions">
            <button class="btn btn-primary btn-block btn-action" data-id="${job.assetId}">Update Status / Log</button>
          </div>
        </div>
      `;
    }).join('');

    // Attach click listeners to cards
    document.querySelectorAll('.btn-action').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const id = btn.dataset.id;
        const job = state.jobs.find(j => j.assetId === id);
        if (job) openModal(job);
      });
    });
  }

  function getBadgeClass(queue) {
    if (typeof queue === 'string') {
      const q = queue.toLowerCase();
      if (q.includes('test')) return 'badge-testing';
      if (q.includes('repair')) return 'badge-repair';
      if (q.includes('sale')) return 'badge-ready';
      return 'badge-intake';
    }
    // Enum integer fallback
    if (queue === 1) return 'badge-testing';
    if (queue === 4 || queue === 8) return 'badge-repair';
    if (queue === 6 || queue === 4) return 'badge-ready';
    return 'badge-intake';
  }

  function getQueueName(queue) {
    if (typeof queue === 'string') return queue.toUpperCase();
    const map = {
      0: 'INTAKE',
      1: 'TESTING',
      2: 'AWAITING PARTS',
      3: 'IN REPAIR',
      4: 'READY TO SELL',
      5: 'SOLD'
    };
    return map[queue] || 'ASSIGNED';
  }

  // ── Status Update Modal ─────────────────────────────────────────────────────
  function openModal(job) {
    state.selectedJob = job;
    state.selectedStatus = null;
    el.modalTitle.textContent = `Update: ${job.serialNumber}`;
    el.modalSubtitle.textContent = `${job.model} (${job.assetTag})`;
    el.modalNotes.value = '';

    el.statusBtns.forEach(b => b.classList.remove('selected'));
    el.modal.classList.remove('hidden');
  }

  function closeModal() {
    el.modal.classList.add('hidden');
    state.selectedJob = null;
    state.selectedStatus = null;
  }

  async function handleSaveStatus() {
    if (!state.selectedJob) return;

    const newStatus = state.selectedStatus;
    const notes = el.modalNotes.value.trim();

    if (!newStatus && !notes) {
      closeModal();
      return;
    }

    try {
      el.modalSave.textContent = 'Saving...';
      el.modalSave.disabled = true;

      const payload = {
        actor: state.user?.name || 'Technician'
      };
      if (newStatus) payload.lifecycleQueue = newStatus;
      if (notes) payload.notes = notes;

      const res = await fetch(`/api/v1/assets/${state.selectedJob.assetId}`, {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${state.token}`
        },
        body: JSON.stringify(payload)
      });

      if (!res.ok) throw new Error('Status update failed');

      closeModal();
      await fetchJobs(true);
    } catch (err) {
      alert(`Could not update asset: ${err.message}`);
    } finally {
      el.modalSave.textContent = 'Save & Push';
      el.modalSave.disabled = false;
    }
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  // Kick off
  document.addEventListener('DOMContentLoaded', init);
})();
