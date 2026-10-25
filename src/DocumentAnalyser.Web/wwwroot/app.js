// Plain JavaScript, no framework or build step. Edit and refresh.
const $ = (id) => document.getElementById(id);
let defaultPrompts = null;

// ---------- Startup: show connection details + load default prompts ----------
async function loadConfig() {
    const config = await (await fetch('/api/config')).json();
    defaultPrompts = config.prompts;
    resetPrompts();
    $('extensions').textContent = `(${config.supportedExtensions.join(', ')})`;
    $('file').accept = config.supportedExtensions.join(',');
    $('maxReviewRounds').value = config.maxReviewRounds;

    $('connection').className = 'connection ' + (config.isConfigured ? 'ok' : 'bad');
    $('connection').innerHTML = config.isConfigured
        ? `<b>Foundry:</b> ${esc(config.endpoint)} · <b>Agents:</b> ${esc(config.chatDeployment)} · ` +
          `<b>Judge:</b> ${esc(config.evaluatorDeployment)} · <b>Auth:</b> ${esc(config.authMode)} · ` +
          `<b>Tracing:</b> ${config.tracingEnabled ? 'on' : 'off'}`
        : '⚠ Foundry isn\'t configured yet. Set Foundry:Endpoint and Foundry:ChatDeployment (README step 4), then restart.';
}

function resetPrompts() {
    $('plannerPrompt').value = defaultPrompts.planner;
    $('analystPrompt').value = defaultPrompts.analyst;
    $('reviewerPrompt').value = defaultPrompts.reviewer;
}

// ---------- Run ----------
async function run() {
    const form = new FormData();
    const file = $('file').files[0];
    if (file) form.append('file', file);
    form.append('documentText', $('documentText').value);
    form.append('instructions', $('instructions').value);
    form.append('maxReviewRounds', $('maxReviewRounds').value);
    form.append('runEvaluation', $('runEvaluation').checked);
    form.append('expectedAnswer', $('expectedAnswer').value);
    // Only send prompts that were changed, so edits to /Prompts/*.md still apply otherwise.
    for (const name of ['planner', 'analyst', 'reviewer']) {
        const value = $(name + 'Prompt').value;
        if (value !== defaultPrompts[name]) form.append(name + 'Prompt', value);
    }

    $('run').disabled = true;
    $('timeline').innerHTML = '';
    $('evaluation').innerHTML = '<p class="muted">Waiting for agents…</p>';
    $('final').innerHTML = '<p class="muted">Working…</p>';
    const started = performance.now();

    try {
        const response = await fetch('/api/analyse', { method: 'POST', body: form });
        // Read the Server-Sent Events stream: each "data: {...}" line is one PipelineEvent.
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        while (true) {
            const { value, done } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            let index;
            while ((index = buffer.indexOf('\n\n')) >= 0) {
                const chunk = buffer.slice(0, index);
                buffer = buffer.slice(index + 2);
                if (chunk.startsWith('data: ')) handleEvent(JSON.parse(chunk.slice(6)));
            }
        }
    } catch (err) {
        handleEvent({ type: 'error', message: err.message });
    } finally {
        $('run').disabled = false;
        addTimelineNote(`Finished in ${((performance.now() - started) / 1000).toFixed(1)}s`);
    }
}

// ---------- Rendering events ----------
const cards = {}; // agent+round → card element, so "done" replaces "running"

function handleEvent(e) {
    console.log('event', e); // open dev tools (F12) to see the raw events
    switch (e.type) {
        case 'document':
            addTimelineNote(`📄 ${esc(e.message)}: ${e.documentChars.toLocaleString()} characters` +
                (e.truncated ? ' (truncated, see Foundry:MaxDocumentChars)' : ''));
            break;
        case 'agent':
            renderAgent(e);
            break;
        case 'evaluation':
            if (e.status === 'running') $('evaluation').innerHTML = '<p class="muted">⏳ Evaluators are judging the final answer…</p>';
            else renderScores(e.scores);
            break;
        case 'final':
            $('final').innerHTML = `<pre>${esc(e.output)}</pre>`;
            break;
        case 'error':
            $('final').innerHTML = `<p class="error">❌ ${esc(e.message)}</p>`;
            addTimelineNote(`<span class="error">❌ ${esc(e.message)}</span>`);
            break;
    }
}

function renderAgent(e) {
    const key = `${e.agent}-${e.round}`;
    let card = cards[key];
    if (!card || !card.isConnected) {
        card = document.createElement('div');
        cards[key] = card;
        $('timeline').appendChild(card);
    }
    card.className = `card ${e.agent.toLowerCase()} ${e.status}`;

    const title = e.agent + (e.round ? ` · round ${e.round}` : '');
    const stats = e.status === 'done'
        ? `${(e.durationMs / 1000).toFixed(1)}s · ${e.inputTokens ?? '?'} in / ${e.outputTokens ?? '?'} out tokens`
        : '⏳ thinking…';

    let review = '';
    if (e.review) {
        review = `<div class="verdict ${e.review.approved ? 'approved' : 'rejected'}">
            ${e.review.approved ? '✅ Approved' : '↩ Sent back'} · score ${e.review.score}/5
            ${e.review.issues.length ? `<ul>${e.review.issues.map(i => `<li>${esc(i)}</li>`).join('')}</ul>` : ''}
            <p>${esc(e.review.feedback)}</p></div>`;
    }

    card.innerHTML = `
        <div class="card-head"><b>${title}</b><span class="muted">${stats}</span></div>
        ${review}
        ${e.output && !e.review ? `<pre class="output">${esc(e.output)}</pre>` : ''}
        <details><summary>System instructions</summary><pre>${esc(e.instructions)}</pre></details>
        <details><summary>Prompt sent (${e.prompt.length.toLocaleString()} chars)</summary><pre>${esc(e.prompt)}</pre></details>
        ${e.review ? `<details><summary>Raw JSON response</summary><pre>${esc(e.output)}</pre></details>` : ''}`;
}

function renderScores(scores) {
    if (!scores?.length) { $('evaluation').innerHTML = '<p class="muted">No scores returned.</p>'; return; }
    $('evaluation').innerHTML = `<table class="scores">
        <thead><tr><th>Metric</th><th>Score</th><th>Rating</th><th>Reason</th></tr></thead>
        <tbody>${scores.map(s => `
            <tr class="${s.failed ? 'failed' : ''}">
                <td>${esc(s.name)}</td>
                <td class="num">${s.value ?? '-'}</td>
                <td>${esc(s.rating ?? '')}${s.failed ? ' ❌' : ''}</td>
                <td>${esc(s.reason ?? '')}${s.diagnostics.map(d => `<div class="${d.startsWith('Informational') ? 'muted' : 'error'}">${esc(d)}</div>`).join('')}</td>
            </tr>`).join('')}
        </tbody></table>`;
}

function addTimelineNote(html) {
    const p = document.createElement('p');
    p.className = 'note';
    p.innerHTML = html;
    $('timeline').appendChild(p);
}

function esc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

$('run').addEventListener('click', run);
$('resetPrompts').addEventListener('click', resetPrompts);
loadConfig();
