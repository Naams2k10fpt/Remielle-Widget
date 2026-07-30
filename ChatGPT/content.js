// =============================================================
// GPTielle — AI Assistant Widget for chatgpt.com
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

// --- Runtime state ---
let currentState = STATES.WAITING;
let widgetImg    = null;
let aiObserver   = null;
let typingTimeout = null;
let isObserving  = false;


// =============================================================
// Widget UI
// =============================================================

function createWidget() {
  if (document.getElementById('gptielle-widget-container')) return;

  const container = document.createElement('div');
  container.id = 'gptielle-widget-container';

  widgetImg = document.createElement('img');
  widgetImg.src = ASSETS.WAITING;
  widgetImg.alt = 'GPTielle - AI Status';
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
// ChatGPT-specific DOM helpers
//
// ChatGPT (chatgpt.com) key landmarks — stable attributes preferred:
//
//  Input box   : div[id="prompt-textarea"][contenteditable]
//                OR .ProseMirror[contenteditable="true"] (fallback)
//
//  Send button : button[data-testid="send-button"]
//                OR button[aria-label*="Send" i]
//
//  Stop button : button[data-testid="stop-button"]
//                (present ONLY while AI is generating)
//
//  AI messages : [data-message-author-role="assistant"]
//                 → streaming text lives inside .markdown, .prose, or
//                   direct text descendants of this container
//
// ChatGPT does NOT have a "thinking" block separate from the reply,
// so we don't need to filter anything out — any mutation inside an
// assistant message bubble is valid "AI typing" signal.
// =============================================================

// Selector lists — ordered by stability (most stable first)
const INPUT_SELECTORS = [
  '#prompt-textarea[contenteditable]',
  '.ProseMirror[contenteditable="true"]',
  'div[contenteditable="true"][data-placeholder]',
  'div[contenteditable="true"]',
];

const SEND_SELECTORS = [
  'button[data-testid="send-button"]',
  'button[aria-label*="send" i]',
  'form button[type="submit"]',
];

const STOP_SELECTOR = 'button[data-testid="stop-button"]';

function getElement(selectors) {
  for (const sel of selectors) {
    const el = document.querySelector(sel);
    if (el) return el;
  }
  return null;
}

function getInputEl()  { return getElement(INPUT_SELECTORS); }
function getSendBtn()  { return getElement(SEND_SELECTORS); }
function isStopVisible() { return !!document.querySelector(STOP_SELECTOR); }

// Returns true when the mutated element is inside an assistant message
function isAssistantResponseMutation(mutation) {
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

  // Must be inside an assistant turn
  const inAssistant = el.closest('[data-message-author-role="assistant"]');
  if (!inAssistant) return false;

  // Must carry actual text content
  return (el.textContent || '').trim().length > 0;
}


// =============================================================
// AI typing detection
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
      if (isAssistantResponseMutation(m)) {
        hasOutput = true;
        break;
      }
    }

    if (!hasOutput) return;

    // First real text → THINKING → TYPING
    if (currentState === STATES.AI_THINKING) {
      setState(STATES.AI_TYPING);
    }

    // Debounce: quiet for 1.2 s AND stop button gone → AI done
    if (currentState === STATES.AI_TYPING) {
      clearTimeout(typingTimeout);
      typingTimeout = setTimeout(() => {
        if (!isStopVisible()) {
          setState(STATES.AI_COMPLETE);
          stopAIObserver();
          scheduleReset();
        }
        // If stop button still visible, keep waiting
      }, 1200);
    }
  });

  aiObserver.observe(root, { childList: true, characterData: true, subtree: true });

  // Safety fallback: 2 minutes max, then give up
  clearTimeout(typingTimeout);
  typingTimeout = setTimeout(() => {
    if (currentState === STATES.AI_THINKING || currentState === STATES.AI_TYPING) {
      setState(STATES.WAITING);
      stopAIObserver();
    }
  }, 120_000);
}

function stopAIObserver() {
  if (aiObserver) {
    aiObserver.disconnect();
    aiObserver = null;
  }
  isObserving = false;
  clearTimeout(typingTimeout);
}

function scheduleReset() {
  setTimeout(() => {
    if (currentState === STATES.AI_COMPLETE) setState(STATES.WAITING);
  }, 3000);
}


// =============================================================
// Submit detection — two paths: Enter key + send button click
// =============================================================

function onSubmit() {
  const input = getInputEl();
  const text  = (input?.textContent || input?.value || '').trim();
  // Require user was typing OR input has content (handles programmatic sends)
  if (currentState !== STATES.USER_TYPING && text.length === 0) return;

  setState(STATES.AI_THINKING);
  startAIObserver();
}


// =============================================================
// User interaction detection
// =============================================================

function handleInputEvent(target) {
  if (!target) return;
  if (!target.isContentEditable && target.tagName !== 'TEXTAREA') return;

  const text    = (target.textContent || target.value || '').trim();
  const aiActive = currentState === STATES.AI_THINKING || currentState === STATES.AI_TYPING;
  if (aiActive) return;

  setState(text.length > 0 ? STATES.USER_TYPING : STATES.WAITING);
}

function setupUserDetection() {
  // Text input / deletion
  document.addEventListener('input',  (e) => handleInputEvent(e.target), true);
  document.addEventListener('keyup',  (e) => {
    if (e.key === 'Backspace' || e.key === 'Delete') handleInputEvent(e.target);
  }, true);

  // Enter key — ChatGPT sends on plain Enter (Shift+Enter = newline)
  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' || e.shiftKey) return;
    const t = e.target;
    if (!t.isContentEditable && t.tagName !== 'TEXTAREA') return;
    const text = (t.textContent || t.value || '').trim();
    if (text.length > 0) onSubmit();
  }, true);

  // Send button click
  document.addEventListener('click', (e) => {
    let el = e.target;
    while (el && el !== document.body) {
      const isSend =
        el.matches?.('button[data-testid="send-button"]') ||
        (el.tagName === 'BUTTON' && (el.getAttribute('aria-label') || '').toLowerCase().includes('send'));

      if (isSend) {
        if (currentState === STATES.USER_TYPING) {
          // Small delay — let React clear the input first
          setTimeout(() => {
            const input = getInputEl();
            const empty = !(input?.textContent || input?.value || '').trim();
            if (empty) onSubmit();
          }, 150);
        }
        break;
      }
      el = el.parentElement;
    }
  }, true);

  // Stop button disappearing = another signal that AI finished
  // (covers edge cases where MutationObserver timeout fires too late)
  const stopPoller = setInterval(() => {
    if ((currentState === STATES.AI_THINKING || currentState === STATES.AI_TYPING) && !isStopVisible()) {
      // Extra check: make sure it's truly gone (not just not rendered yet)
      setTimeout(() => {
        if (!isStopVisible() && (currentState === STATES.AI_THINKING || currentState === STATES.AI_TYPING)) {
          setState(STATES.AI_COMPLETE);
          stopAIObserver();
          scheduleReset();
        }
      }, 400);
    }
  }, 800);

  // Clean up poller if extension context invalidates (page unload)
  window.addEventListener('beforeunload', () => clearInterval(stopPoller));
}


// =============================================================
// SPA navigation: ChatGPT changes chats without full reload
// =============================================================

let lastUrl = location.href;
new MutationObserver(() => {
  if (location.href !== lastUrl) {
    lastUrl = location.href;
    stopAIObserver();
    currentState = STATES.WAITING;
    if (widgetImg) widgetImg.src = ASSETS.WAITING;
  }
}).observe(document, { subtree: true, childList: true });


// =============================================================
// Boot
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
