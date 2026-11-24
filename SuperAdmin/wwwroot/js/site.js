// Super Admin - small page helpers (no framework).
(function () {
    var root = document.documentElement;

    // Light / dark switch. Remembered per browser; storage may be blocked.
    document.addEventListener('click', function (e) {
        var t = e.target.closest('[data-theme-toggle]');
        if (!t) return;
        var dark = root.getAttribute('data-theme') === 'dark' ||
            (!root.getAttribute('data-theme') && window.matchMedia('(prefers-color-scheme: dark)').matches);
        var next = dark ? 'light' : 'dark';
        root.setAttribute('data-theme', next);
        try { localStorage.setItem('sa.theme', next); } catch (err) { }
    });

    // On narrow screens the tab row scrolls - bring the open tab into view.
    var activeTab = document.querySelector('.tabs .tab.active');
    if (activeTab && activeTab.parentElement.scrollWidth > activeTab.parentElement.clientWidth)
        activeTab.parentElement.scrollLeft = activeTab.offsetLeft - 12;

    // Company selector: open the same section for the chosen company.
    var pick = document.querySelector('[data-company-pick]');
    if (pick) pick.addEventListener('change', function () {
        if (!pick.value) return;
        var tab = pick.getAttribute('data-tab') || 'words';
        if (tab === 'rules') tab = 'words';
        window.location.href = '/company/' + encodeURIComponent(pick.value) + '/' + encodeURIComponent(tab);
    });

    // Phone menu.
    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-open-nav]')) document.body.classList.add('nav-open');
        else if (e.target.closest('[data-close-nav]')) document.body.classList.remove('nav-open');
    });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') document.body.classList.remove('nav-open');
    });

    // Branding: live preview while typing. Only values that would be accepted on
    // save reach the preview (hex colours, plain https/http addresses).
    var brandForm = document.getElementById('brand-form');
    var preview = document.getElementById('brand-preview');
    if (brandForm && preview) {
        var isHex = function (v) { return /^#[0-9A-Fa-f]{6}$/.test(v); };
        var isUrl = function (v) { return /^https?:\/\/[^\s"'<>()]+$/i.test(v); };
        var val = function (name) {
            var el = brandForm.querySelector('[name="' + name + '"]');
            var v = (el.value || '').trim();
            return v || (el.getAttribute('placeholder') === 'as today' ? '' : el.getAttribute('placeholder') || '');
        };
        var update = function () {
            var p = val('primary'), s = val('secondary');
            preview.style.setProperty('--bp-primary', isHex(p) ? p : '#1F5FBF');
            preview.style.setProperty('--bp-secondary', isHex(s) ? s : '#00254E');
            ['primary', 'secondary'].forEach(function (n) {
                var sw = brandForm.querySelector('[data-swatch-for="' + n + '"]');
                if (sw) sw.style.background = isHex(val(n)) ? val(n) : 'transparent';
            });
            var logo = val('logo'), img = preview.querySelector('[data-bp-logo]'), mark = preview.querySelector('[data-bp-mark]');
            if (isUrl(logo)) { if (img.getAttribute('src') !== logo) { img.src = logo; img.hidden = false; mark.hidden = true; } }
            else { img.removeAttribute('src'); img.hidden = true; mark.hidden = false; }
            var bg = val('loginBg'), side = preview.querySelector('[data-bp-side]');
            side.style.backgroundImage = isUrl(bg) ? 'linear-gradient(150deg,rgba(0,0,0,.55),rgba(0,0,0,.35)),url("' + bg + '")' : '';
            preview.querySelector('[data-bp-tagline]').textContent = val('tagline') || 'Sign in to continue';
            document.querySelector('[data-bp-footer]').textContent = val('footer');
            document.querySelector('[data-bp-copyright]').textContent = val('copyright');
        };
        brandForm.addEventListener('input', update);
        // A logo address that does not load shows the company letter instead of a gap.
        var logoImg = preview.querySelector('[data-bp-logo]');
        var showMark = function () { logoImg.hidden = true; preview.querySelector('[data-bp-mark]').hidden = false; };
        logoImg.addEventListener('error', showMark);
        if (logoImg.getAttribute('src') && logoImg.complete && logoImg.naturalWidth === 0) showMark();
        var clear = brandForm.querySelector('[data-clear-brand]');
        if (clear) clear.addEventListener('click', function () {
            if (!window.confirm('Clear all branding for this company? The panels go back to the default look.')) return;
            brandForm.querySelectorAll('input[name]').forEach(function (i) { if (i.type !== 'hidden') i.value = ''; });
            brandForm.classList.remove('dirty');   // .submit() skips the submit event - no "unsaved" warning for this
            brandForm.submit();
        });
    }

    // Advanced panel settings: confirm before changing away from "As today".
    document.addEventListener('submit', function (e) {
        var f = e.target;
        if (f.getAttribute('data-advanced') !== '1') return;
        var v = f.querySelector('input[name=value]:checked') || f.querySelector('input[name=value]');
        if (v && v.value && !window.confirm('This setting changes which query / save / layout the page uses. If this company\'s data is different, the page can stop working. Test it on a test company first. Continue?'))
            e.preventDefault();
    });

    // Ask before a Reset (buttons marked data-confirm-btn).
    document.addEventListener('submit', function (e) {
        if (e.submitter && e.submitter.hasAttribute('data-confirm-btn') &&
            !window.confirm('Reset this to how the panel works today?')) e.preventDefault();
    });

    // ---- easier to use ----------------------------------------------------

    var store = {
        get: function (k) { try { return sessionStorage.getItem(k); } catch (e) { return null; } },
        set: function (k, v) { try { sessionStorage.setItem(k, v); } catch (e) { } },
        del: function (k) { try { sessionStorage.removeItem(k); } catch (e) { } }
    };

    // "Show codes": technical names for developers, hidden by default.
    var techBox = document.querySelector('[data-tech-toggle]');
    if (techBox) {
        techBox.checked = root.classList.contains('show-tech');
        techBox.addEventListener('change', function () {
            root.classList.toggle('show-tech', techBox.checked);
            try { localStorage.setItem('sa.tech', techBox.checked ? '1' : '0'); } catch (e) { }
        });
    }

    // Toasts: "saved" goes away by itself, errors stay until closed.
    document.querySelectorAll('[data-toast-auto]').forEach(function (t) {
        setTimeout(function () { t.classList.add('gone'); setTimeout(function () { t.remove(); }, 450); }, 5000);
    });
    document.addEventListener('click', function (e) {
        var x = e.target.closest('[data-toast-close]');
        if (x) x.closest('.toast').remove();
    });

    // Company list: the whole row opens the company.
    document.addEventListener('click', function (e) {
        var tr = e.target.closest('tr[data-href]');
        if (tr && !e.target.closest('a, button, input, select, label')) window.location.href = tr.getAttribute('data-href');
    });

    // Which row a form is: its action + its hidden ids (not the security token).
    var postForms = function () {
        return Array.prototype.filter.call(document.querySelectorAll('form'), function (f) {
            return (f.getAttribute('method') || '').toLowerCase() === 'post' && !/\/account\/logout$/i.test(f.getAttribute('action') || '');
        });
    };
    var signature = function (f) {
        var parts = [f.getAttribute('action') || ''];
        f.querySelectorAll('input[type=hidden]').forEach(function (h) {
            if (h.name && h.name !== '__RequestVerificationToken') parts.push(h.name + '=' + h.value);
        });
        return parts.join('|');
    };

    // Rows typed in but not saved: mark them, and warn before leaving the page.
    var submitting = false;
    postForms().forEach(function (f) {
        var actions = f.querySelector('.actions');
        if (actions && (f.classList.contains('setting') || f.classList.contains('menu-row'))) {
            var note = document.createElement('span');
            note.className = 'dirty-note';
            note.textContent = 'Not saved yet →';
            actions.insertBefore(note, actions.firstChild);
        }
        var mark = function (e) { if (e.target.name && e.target.type !== 'hidden') f.classList.add('dirty'); };
        f.addEventListener('input', mark);
        f.addEventListener('change', mark);
    });
    document.addEventListener('submit', function (e) {
        var f = e.target;
        if (e.defaultPrevented || (f.getAttribute('method') || '').toLowerCase() !== 'post') return;
        var others = postForms().filter(function (o) { return o !== f && o.classList.contains('dirty'); });
        if (others.length && !window.confirm('You also changed ' + others.length + ' other row(s) that are not saved yet. Those changes will be lost.\n\nSave only this row?')) {
            e.preventDefault();
            others[0].scrollIntoView({ block: 'center' });
            return;
        }
        submitting = true;
        // Come back to this same row after the page reloads.
        store.set('sa.lastSave', JSON.stringify({ path: location.pathname, y: window.scrollY, sig: signature(f), at: Date.now() }));
    });
    // Runs after the other submit checks (confirm dialogs) - if one cancelled, forget the row.
    document.addEventListener('submit', function (e) { if (e.defaultPrevented) { submitting = false; store.del('sa.lastSave'); } });
    window.addEventListener('beforeunload', function (e) {
        if (submitting) return;
        if (postForms().some(function (f) { return f.classList.contains('dirty'); })) { e.preventDefault(); e.returnValue = ''; }
    });

    // After a save: open the page at the row that was saved and flash it green.
    var last = store.get('sa.lastSave');
    if (last) {
        store.del('sa.lastSave');
        try {
            var s = JSON.parse(last);
            if (s.path === location.pathname && Date.now() - s.at < 60000) {
                var row = postForms().filter(function (f) { return signature(f) === s.sig; })[0];
                var saved = document.querySelector('[data-toast-auto]');
                if (row && (row.classList.contains('setting') || row.classList.contains('menu-row'))) {
                    row.scrollIntoView({ block: 'center' });
                    if (saved) row.classList.add('just-saved');
                } else {
                    window.scrollTo(0, s.y);
                }
            }
        } catch (err) { }
    }

    // "Find on this page" + "Only what is set" above long lists of rows.
    document.querySelectorAll('.list').forEach(function (list) {
        var rows = Array.prototype.filter.call(list.children, function (el) { return el.matches('form.setting, form.menu-row'); });
        if (rows.length < 6) return;
        var anySet = rows.some(function (r) { return r.querySelector('.chip.brand'); });
        var bar = document.createElement('div');
        bar.className = 'findbar';
        bar.innerHTML =
            '<input type="search" class="input" placeholder="Find on this page - type any word" aria-label="Find on this page" />' +
            (anySet ? '<label><input type="checkbox" data-only-set /> Only what is changed for this company</label>' : '') +
            '<span class="faint small" data-find-count></span>';
        list.parentNode.insertBefore(bar, list);
        var empty = document.createElement('p');
        empty.className = 'find-empty';
        empty.hidden = true;
        empty.textContent = 'Nothing matches. Try another word, or clear the box.';
        list.parentNode.insertBefore(empty, list.nextSibling);

        var box = bar.querySelector('input[type=search]'), onlySet = bar.querySelector('[data-only-set]'), count = bar.querySelector('[data-find-count]');
        var textOf = function (r) {
            var t = r.textContent;
            r.querySelectorAll('input:not([type=hidden]), textarea').forEach(function (i) { t += ' ' + (i.value || '') + ' ' + (i.placeholder || ''); });
            return t.toLowerCase();
        };
        var apply = function () {
            var words = box.value.toLowerCase().split(/\s+/).filter(Boolean);
            var shown = 0;
            rows.forEach(function (r) {
                var t = textOf(r);
                var ok = words.every(function (w) { return t.indexOf(w) >= 0; }) &&
                    (!onlySet || !onlySet.checked || r.querySelector('.chip.brand') || r.classList.contains('dirty'));
                r.classList.toggle('hidden-by-find', !ok);
                if (ok) shown++;
            });
            // A group heading stays only if something under it is shown.
            var heads = list.querySelectorAll('.group-head');
            heads.forEach(function (h) {
                var any = false;
                for (var n = h.nextElementSibling; n && !n.classList.contains('group-head'); n = n.nextElementSibling)
                    if (n.matches('form') && !n.classList.contains('hidden-by-find')) { any = true; break; }
                h.classList.toggle('hidden-by-find', !any);
            });
            var head = list.querySelector('.menu-head');
            if (head) head.classList.toggle('hidden-by-find', shown === 0);
            var filtering = words.length > 0 || (onlySet && onlySet.checked);
            count.textContent = filtering ? shown + ' of ' + rows.length + ' shown' : rows.length + ' rows';
            empty.hidden = shown > 0;
        };
        box.addEventListener('input', apply);
        if (onlySet) onlySet.addEventListener('change', apply);
        apply();
    });
})();
