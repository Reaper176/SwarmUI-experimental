/** Helper class for tracking the user's permissions. */
class Permissions {
    constructor() {
        this.permissions = {};
        this.permissionedDivs = [];
        this.applyCallbacks = [];
        this.hasLoaded = false;
        setTimeout(() => {
            this.gather();
        }, 0);
        document.addEventListener('show.bs.tab', (e) => this.onNavClick(e));
    }

    gather() {
        this.permissionedDivs = [...document.querySelectorAll('[data-requiredpermission]')];
        this.apply();
    }

    updateFrom(set) {
        this.permissions = {};
        for (let key of set) {
            this.permissions[key] = true;
        }
        this.hasLoaded = true;
        this.apply();
    }

    /** Registers a callback to run after each loaded permission set is applied. */
    registerApplyCallback(callback) {
        if (!this.applyCallbacks.includes(callback)) {
            this.applyCallbacks.push(callback);
        }
    }

    apply() {
        if (!this.hasLoaded) {
            return;
        }
        for (let div of this.permissionedDivs) {
            let key = div.dataset.requiredpermission;
            if (!this.hasPermission(key)) {
                div.style.display = 'none';
            }
            else {
                div.style.display = '';
            }
        }
        for (let callback of this.applyCallbacks) {
            callback();
        }
    }

    onNavClick(e) {
        let navItem = findParentOfClass(e.target, 'nav-item');
        if (!navItem) {
            return;
        }
        let key = navItem.dataset.requiredpermission;
        if (key && !this.hasPermission(key)) {
            e.preventDefault();
        }
    }

    hasPermission(key) {
        if (!this.hasLoaded) {
            return true;
        }
        if (key.includes(',')) {
            for (let k of key.split(',')) {
                if (!this.hasPermission(k)) {
                    return false;
                }
            }
            return true;
        }
        return this.permissions[key] || this.permissions['*'];
    }
}

permissions = new Permissions();
