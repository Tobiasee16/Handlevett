/* Handlevett — shared client behaviour.
   Three independent modules: the theme toggle, the warm-up status pill, and the
   plan-building busy indicator. No framework, no build step. */

(function () {
    'use strict';

    // ── Theme toggle ─────────────────────────────────────────────────────────
    // The stored preference is already applied by the inline script in <head>;
    // this only wires up the button and keeps aria-pressed honest.

    var THEME_KEY = 'handlevett-theme';

    function readStoredTheme() {
        try {
            return localStorage.getItem(THEME_KEY);
        } catch (e) {
            return null;
        }
    }

    function storeTheme(value) {
        try {
            localStorage.setItem(THEME_KEY, value);
        } catch (e) {
            // Private browsing or blocked site data — the toggle still works for
            // this page view, it just will not be remembered.
        }
    }

    function currentlyDark() {
        var explicit = document.documentElement.getAttribute('data-theme');
        if (explicit) {
            return explicit === 'dark';
        }
        return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    }

    function initThemeToggle() {
        var button = document.getElementById('theme-toggle');
        if (!button) {
            return;
        }

        button.setAttribute('aria-pressed', String(currentlyDark()));

        button.addEventListener('click', function () {
            var next = currentlyDark() ? 'light' : 'dark';
            document.documentElement.setAttribute('data-theme', next);
            storeTheme(next);
            button.setAttribute('aria-pressed', String(next === 'dark'));
        });

        // Follow the OS while the user has made no explicit choice.
        if (!readStoredTheme() && window.matchMedia) {
            window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function (e) {
                button.setAttribute('aria-pressed', String(e.matches));
            });
        }
    }

    // ── Shared status polling ────────────────────────────────────────────────
    // One poller feeds both the header pill and the plan-building banner, so the
    // two never disagree about what the server is doing.

    var POLL_MS = 2000;
    var TERMINAL = { Ready: true, Failed: true };

    var PHASE_TEXT = {
        NotStarted: 'Starter opp…',
        FetchingPrices: 'Henter matvarepriser…',
        GeneratingRecipes: 'Ollama lager oppskrifter…',
        Ready: 'Klar',
        Failed: 'Kunne ikke hente data'
    };

    var listeners = [];
    var polling = false;

    function onStatus(callback) {
        listeners.push(callback);
    }

    function fetchStatus() {
        return fetch('/api/status', { headers: { Accept: 'application/json' } })
            .then(function (response) {
                return response.ok ? response.json() : null;
            })
            .catch(function () {
                // App restarting (dotnet watch) or offline.
                return null;
            });
    }

    function pump() {
        if (polling) {
            return;
        }
        polling = true;

        function tick() {
            fetchStatus().then(function (status) {
                var keepGoing = false;

                listeners.forEach(function (callback) {
                    if (callback(status) === true) {
                        keepGoing = true;
                    }
                });

                if (keepGoing) {
                    window.setTimeout(tick, POLL_MS);
                } else {
                    polling = false;
                }
            });
        }

        tick();
    }

    // ── Header warm-up pill ──────────────────────────────────────────────────

    function initWarmupPill() {
        var pill = document.getElementById('warmup-pill');
        var text = document.getElementById('warmup-text');
        if (!pill || !text) {
            return;
        }

        // Set by the page when it rendered with no grocery data — if warm-up then
        // finishes successfully we offer a reload rather than doing it under the user.
        var pageRenderedEmpty = document.body.dataset.emptyRender === 'true';
        var settled = false;

        onStatus(function (status) {
            if (!status) {
                return false;
            }

            // A model call in flight outranks the coarse phase: it is the thing that
            // actually takes minutes, so say so and keep polling.
            var working = status.generating || !TERMINAL[status.phase];
            var label = status.generating
                ? PHASE_TEXT.GeneratingRecipes + ' ' + formatElapsed(status.generatingSeconds)
                : (PHASE_TEXT[status.phase] || status.phase);

            // Nothing interesting to report on a warm start.
            if (!working && pill.hidden) {
                return false;
            }

            pill.hidden = false;
            pill.classList.remove('status-pill--working', 'status-pill--ready', 'status-pill--failed');

            if (status.phase === 'Failed') {
                pill.classList.add('status-pill--failed');
            } else if (working) {
                pill.classList.add('status-pill--working');
            } else {
                pill.classList.add('status-pill--ready');
            }

            text.textContent = label;

            if (working) {
                return true;
            }

            if (!settled) {
                settled = true;

                if (status.phase === 'Ready' && pageRenderedEmpty
                    && status.prices && status.prices.itemCount > 0) {
                    showReloadPrompt();
                }

                window.setTimeout(function () {
                    pill.classList.add('status-pill--fading');
                    window.setTimeout(function () {
                        pill.hidden = true;
                        pill.classList.remove('status-pill--fading');
                    }, 400);
                }, 3000);
            }

            return false;
        });
    }

    function formatElapsed(seconds) {
        if (!seconds || seconds < 1) {
            return '';
        }
        if (seconds < 60) {
            return '(' + seconds + ' s)';
        }
        return '(' + Math.floor(seconds / 60) + ' min ' + (seconds % 60) + ' s)';
    }

    function showReloadPrompt() {
        var host = document.getElementById('reload-prompt-slot');
        if (!host || host.dataset.shown === 'true') {
            return;
        }
        host.dataset.shown = 'true';

        var wrapper = document.createElement('div');
        wrapper.className = 'reload-prompt';
        wrapper.setAttribute('role', 'status');

        var message = document.createElement('span');
        message.textContent = 'Matvareprisene er klare.';

        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn-primary selection-bar-action';
        button.textContent = 'Last inn på nytt';
        button.addEventListener('click', function () {
            window.location.reload();
        });

        wrapper.append(message, button);
        host.replaceChildren(wrapper);
    }

    // ── Plan-building busy indicator ─────────────────────────────────────────
    // The planner form is a full-page POST. On a cold cache the request sits inside
    // an Ollama call that can run for up to two minutes, during which the browser
    // shows the old page with no sign that anything is happening. The page stays
    // interactive while it waits, so we can both show a banner and keep polling to
    // report what the server is actually doing.

    function initPlanBusy() {
        var form = document.querySelector('.planner-form');
        var banner = document.getElementById('plan-busy');
        var submit = document.getElementById('plan-submit');

        if (!form || !banner) {
            return;
        }

        var title = document.getElementById('plan-busy-title');
        var detail = document.getElementById('plan-busy-detail');
        var elapsedLabel = document.getElementById('plan-busy-elapsed');

        var startedAt = 0;
        var ticker = null;
        var busy = false;

        function renderElapsed() {
            var seconds = Math.floor((Date.now() - startedAt) / 1000);
            elapsedLabel.textContent = seconds < 1 ? '' : formatElapsed(seconds).replace(/[()]/g, '');

            // Set expectations before the wait starts feeling like a hang.
            if (seconds === 8 && detail) {
                detail.textContent = 'Ollama skriver oppskrifter. Første gang kan ta et par minutter.';
            }
        }

        form.addEventListener('submit', function () {
            // Native validation blocks the submit before this fires, so a busy state
            // here always corresponds to a request that is really on its way.
            if (busy) {
                return;
            }
            busy = true;
            startedAt = Date.now();

            banner.hidden = false;
            elapsedLabel.textContent = '';

            if (submit) {
                submit.dataset.busy = 'true';
                submit.setAttribute('aria-busy', 'true');
                var label = submit.querySelector('.btn-label');
                if (label) {
                    label.textContent = 'Lager plan…';
                }
            }

            var results = document.querySelector('.result-list');
            if (results) {
                results.classList.add('results-stale');
            }

            ticker = window.setInterval(renderElapsed, 1000);

            // Report what the server is doing, not just that we are waiting.
            onStatus(function (status) {
                if (!busy) {
                    return false;
                }
                if (status && status.generating && title) {
                    title.textContent = 'Ollama lager oppskrifter…';
                }
                return true;
            });
            pump();
        });

        // A restored bfcache page (back button) must not keep showing the banner.
        window.addEventListener('pageshow', function (event) {
            if (!event.persisted) {
                return;
            }
            busy = false;
            banner.hidden = true;
            if (ticker) {
                window.clearInterval(ticker);
            }
            if (submit) {
                delete submit.dataset.busy;
                submit.removeAttribute('aria-busy');
                var label = submit.querySelector('.btn-label');
                if (label) {
                    label.textContent = 'Lag middagsplan';
                }
            }
            var results = document.querySelector('.result-list');
            if (results) {
                results.classList.remove('results-stale');
            }
        });
    }

    function init() {
        initThemeToggle();
        initWarmupPill();
        initPlanBusy();
        pump();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
}());
