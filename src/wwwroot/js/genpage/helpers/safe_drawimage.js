// Safe drawImage wrapper to prevent "Passed-in image is \"broken\"" DOMExceptions
(function(){
    try {
        const proto = CanvasRenderingContext2D && CanvasRenderingContext2D.prototype;
        if (!proto) return;
        if (proto._safeDrawImageInstalled) return;

        const original = proto.drawImage;

        proto.drawImage = function() {
            try {
                const img = arguments[0];

                // If the first arg looks like an Image, ensure it's fully loaded and valid
                if (img && (img instanceof HTMLImageElement || (typeof Image !== 'undefined' && img instanceof Image))) {
                    if (!img.complete || (typeof img.naturalWidth === 'number' && img.naturalWidth === 0)) {
                        // Skip drawing broken or not-yet-loaded images
                        return;
                    }
                }

                return original.apply(this, arguments);
            } catch (err) {
                // Log once per image src to help diagnose broken sources without spamming
                try {
                    const src = (img && (img.src || img.currentSrc)) || '(no-src)';
                    proto._safeDrawImageLastLogged = proto._safeDrawImageLastLogged || new Map();
                    const last = proto._safeDrawImageLastLogged.get(src);
                    const now = Date.now();
                    if (!last || (now - last) > 30000) { // throttle 30s
                        proto._safeDrawImageLastLogged.set(src, now);
                        const stack = (new Error()).stack || '';
                        console.warn('safe_drawimage: suppressed drawImage error for', src, err, '\nstack:', stack.split('\n').slice(0,4).join('\n'));
                    }
                } catch (e2) {
                    console.warn('safe_drawimage: suppressed drawImage error (no extra info)', err);
                }
            }
        };

        proto._safeDrawImageInstalled = true;
    } catch (e) {
        console.error('safe_drawimage: failed to install wrapper', e);
    }
})();
