let handler = null;
let dotNetRef = null;

export function register(ref) {
    unregister();
    dotNetRef = ref;
    handler = (e) => {
        const anchor = e.target.closest('a[href]');
        if (!anchor) return;
        if (anchor.target === '_blank' || anchor.hasAttribute('download')) return;
        const href = anchor.getAttribute('href');
        if (!href || !href.startsWith('/')) return;

        e.preventDefault();
        e.stopPropagation();
        dotNetRef.invokeMethodAsync('OnNavigationAttempted', href);
    };
    document.addEventListener('click', handler, true);
}

export function unregister() {
    if (handler) {
        document.removeEventListener('click', handler, true);
        handler = null;
    }
    dotNetRef = null;
}
