// =============================================================
// Claudelle — AI Assistant Widget for claude.ai
// =============================================================

// --- Assets & States ---
const ASSETS = {
  WAITING:     chrome.runtime.getURL('assets/waiting_user_input.gif'),
  USER_TYPING: chrome.runtime.getURL('assets/user_typing.gif'),
  AI_THINKING: chrome.runtime.getURL('assets/ai_thingking.gif'),
  AI_TYPING:   chrome.runtime.getURL('assets/ai_typing.gif'),
  AI_COMPLETE: chrome.runtime.getURL('assets/ai_complete_answer.gif'),
};

const STATES = {
  WAITING:     'WAITING',
  USER_TYPING: 'USER_TYPING',
  AI_THINKING: 'AI_THINKING',
  AI_TYPING:   'AI_TYPING',
  AI_COMPLETE: 'AI_COMPLETE',
};

// --- State ---
let currentState = STATES.WAITING;
let widgetImg = null;
let aiObserver = null;
let typingTimeout = null;
let isObserving = false;


// =============================================================
// Widget UI
// =============================================================

function createWidget() {
  // Avoid duplicates on SPA navigation
  if (document.getElementById('claudelle-widget-container')) return;

  const container = document.createElement('div');
  container.id = 'claudelle-widget-container';

  widgetImg = document.createElement('img');
  widgetImg.src = ASSETS.WAITING;
  widgetImg.alt = 'Claudelle - AI Status';
  widgetImg.draggable = false;

  container.appendChild(widgetImg);
  document.body.appendChild(container);

  makeDraggable(container);
}

function makeDraggable(container) {
  let isDragging = false;
  let startX = 0, startY = 0;
  let initLeft = 0, initTop = 0;

  container.addEventListener('mousedown', (e) => {
    if (e.button !== 0) return;
    isDragging = true;
    container.classList.add('dragging');

    const rect = container.getBoundingClientRect();
    initLeft = rect.left;
    initTop  = rect.top;

    // Switch from bottom/right anchoring to top/left so we can move freely
    container.style.bottom = 'auto';
    container.style.right  = 'auto';
    container.style.left   = `${initLeft}px`;
    container.style.top    = `${initTop}px`;

    startX = e.clientX;
    startY = e.clientY;
    e.preventDefault();
  });

  document.addEventListener('mousemove', (e) => {
    if (!isDragging) return;
    const newLeft = Math.max(0, Math.min(initLeft + (e.clientX - startX), window.innerWidth  - container.offsetWidth));
    const newTop  = Math.max(0, Math.min(initTop  + (e.clientY - startY), window.innerHeight - container.offsetHeight));
    container.style.left = `${newLeft}px`;
    container.style.top  = `${newTop}px`;
  });

  document.addEventListener('mouseup', () => {
    if (isDragging) {
      isDragging = false;
      container.classList.remove('dragging');
    }
  });
}

function setState(newState) {
  if (currentState === newState) return;
  currentState = newState;
  if (widgetImg) widgetImg.src = ASSETS[newState];
}


// =============================================================
// Claude-specific selectors
//
// Claude.ai is a React SPA. Key landmarks:
//   Input box : div[contenteditable="true"] inside the composer
//   Send btn  : button[aria-label="Send message"]
//   AI stream : div.font-claude-message  (the streaming text container)
//               OR any descendant of div[data-testid="chat-message-content"]
//
// We deliberately avoid fragile long class chains and rely on
// stable attributes + parent relationships wherever possible.
// =============================================================

// Returns true when the element is inside Claude's streaming output area
// and NOT inside a "thinking" block (Claude extended thinking).
function isInsideClaudeResponse(el) {
  if (!el) return false;

  // Must be inside a recognized response container
  const inResponse = el.closest(
    '[data-testid="chat-message-content"], ' +   // main message wrapper
    '.font-claude-message, ' +                   // streaming text root
    '.prose'                                     // markdown prose block
  );
  if (!inResponse) return false;

  // Must NOT be inside a "thinking" block
  const inThinking = el.closest(
    '[data-testid="thinking-block"], ' +
    '.thinking-block, ' +
    '[data-is-thinking="true"]'
  );
  if (inThinking) return false;

  return true;
}

function isResponseMutation(mutation) {
  let el = null;

  if (mutation.type === 'characterData') {
    el = mutation.target.parentElement;
  } else if (mutation.type === 'childList') {
    for (const node of mutation.addedNodes) {
      el = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
      if (el) break;
    }
  }

  if (!el) return false;
  if (!isInsideClaudeResponse(el)) return false;

  const text = (el.textContent || '').trim();
  return text.length > 0;
}


// =============================================================
// AI typing detection (MutationObserver)
// =============================================================

function startAIObserver() {
  if (isObserving) return;
  isObserving = true;

  const root = document.querySelector('main') ||
               document.querySelector('[role="main"]') ||
               document.body;

  aiObserver = new MutationObserver((mutations) => {
    let hasOutput = false;

    for (const m of mutations) {
      if (isResponseMutation(m)) {
        hasOutput = true;
        break;
      }
    }

    if (!hasOutput) return;

    // Transition: THINKING → TYPING on first real text
    if (currentState === STATES.AI_THINKING) {
      setState(STATES.AI_TYPING);
    }

    // Debounce: if DOM goes quiet for 1 s → AI finished
    if (currentState === STATES.AI_TYPING) {
      clearTimeout(typingTimeout);
      typingTimeout = setTimeout(() => {
        setState(STATES.AI_COMPLETE);
        stopAIObserver();
      }, 1000);
    }
  });

  aiObserver.observe(root, { childList: true, characterData: true, subtree: true });

  // Safety fallback: if nothing happens in 90 s, give up
  clearTimeout(typingTimeout);
  typingTimeout = setTimeout(() => {
    if (currentState === STATES.AI_THINKING) {
      setState(STATES.WAITING);
      stopAIObserver();
    }
  }, 90_000);
}

function stopAIObserver() {
  if (aiObserver) {
    aiObserver.disconnect();
    aiObserver = null;
  }
  isObserving = false;
  clearTimeout(typingTimeout);
}

// After AI_COMPLETE, go back to WAITING after a short pause
// so the user sees the "done" gif, then it resets.
function scheduleReset() {
  setTimeout(() => {
    if (currentState === STATES.AI_COMPLETE) {
      setState(STATES.WAITING);
    }
  }, 3000);
}


// =============================================================
// User interaction detection
// =============================================================

function getActiveInput() {
  // Claude's composer: a ProseMirror contenteditable inside the footer area
  return document.querySelector(
    'div[contenteditable="true"][data-placeholder], ' +
    'div[contenteditable="true"].ProseMirror, ' +
    'div[contenteditable="true"]'               // generic fallback
  );
}

function handleInputEvent(target) {
  if (!target) return;
  if (!target.isContentEditable && target.tagName !== 'TEXTAREA' && target.tagName !== 'INPUT') return;

  const text = (target.textContent || target.value || '').trim();
  const aiActive = currentState === STATES.AI_THINKING || currentState === STATES.AI_TYPING;

  if (aiActive) return; // Don't interrupt AI states

  setState(text.length > 0 ? STATES.USER_TYPING : STATES.WAITING);
}

function onSubmit() {
  // Only trigger if user was typing and there's actual content
  const input = getActiveInput();
  const text = (input?.textContent || input?.value || '').trim();
  if (text.length === 0 && currentState !== STATES.USER_TYPING) return;

  setState(STATES.AI_THINKING);
  startAIObserver();
}

function setupUserDetection() {
  // Text input / deletion
  document.addEventListener('input', (e) => handleInputEvent(e.target), true);
  document.addEventListener('keyup', (e) => {
    if (e.key === 'Backspace' || e.key === 'Delete') handleInputEvent(e.target);
  }, true);

  // Enter key to send (Claude uses Shift+Enter for newline)
  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' || e.shiftKey) return;
    const t = e.target;
    if (!t.isContentEditable && t.tagName !== 'TEXTAREA') return;
    const text = (t.textContent || t.value || '').trim();
    if (text.length > 0) onSubmit();
  }, true);

  // Click on send button
  document.addEventListener('click', (e) => {
    let el = e.target;
    while (el && el !== document.body) {
      const label = el.getAttribute('aria-label') || '';
      const isBtn = el.tagName === 'BUTTON' || el.getAttribute('role') === 'button';

      if (isBtn && (label.toLowerCase().includes('send') || label === '')) {
        if (currentState === STATES.USER_TYPING) {
          // Small delay to let React clear the input first
          setTimeout(() => {
            const input = getActiveInput();
            const empty = !(input?.textContent || input?.value || '').trim();
            if (empty) onSubmit();
          }, 150);
        }
        break;
      }
      el = el.parentElement;
    }
  }, true);
}


// =============================================================
// SPA navigation: Claude changes conversation without full reload
// =============================================================

function reinitialize() {
  stopAIObserver();
  currentState = STATES.WAITING;
  if (widgetImg) widgetImg.src = ASSETS.WAITING;
}

// Watch URL changes (pushState / replaceState)
let lastUrl = location.href;
new MutationObserver(() => {
  if (location.href !== lastUrl) {
    lastUrl = location.href;
    reinitialize();
  }
}).observe(document, { subtree: true, childList: true });


// =============================================================
// Boot — wait for body to exist (document_end guarantees it)
// =============================================================

function boot() {
  createWidget();
  setupUserDetection();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot);
} else {
  boot();
}
