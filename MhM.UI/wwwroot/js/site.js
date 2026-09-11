function requestLogin(route, email, password, RememberMe) {
    return fetch(route, {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        credentials: "include",
        body: JSON.stringify({ email, password, RememberMe })
    })
        .then(res => {
            if (res.ok) {
                return true;
            } else {
                return false;
            }
        });
}

window.browserLocation = {
    getCurrent: function () {
        return new Promise((resolve) => {
            if (!navigator.geolocation) {
                resolve(null);
                return;
            }

            navigator.geolocation.getCurrentPosition(
                (position) => resolve({
                    latitude: position.coords.latitude,
                    longitude: position.coords.longitude
                }),
                () => resolve(null),
                {
                    enableHighAccuracy: true,
                    timeout: 10000,
                    maximumAge: 60000
                });
        });
    }
};

window.theme = {
    syncToggles: function () {
        const darkMode = document.documentElement.dataset.theme === "dark";
        document.querySelectorAll(".theme-toggle").forEach(toggle => {
            toggle.setAttribute("aria-pressed", String(darkMode));
            toggle.setAttribute("aria-label", darkMode ? "Dunkelmodus ausschalten" : "Dunkelmodus einschalten");
        });
    },
    apply: function (theme, persist = false) {
        const normalizedTheme = theme === "dark" ? "dark" : "light";
        document.documentElement.dataset.theme = normalizedTheme;

        if (persist) {
            try {
                localStorage.setItem("mhm-theme", normalizedTheme);
            } catch {
                // The selected theme still applies for the current page.
            }
        }

        this.syncToggles();
    },
    toggle: function () {
        const nextTheme = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
        this.apply(nextTheme, true);
    }
};

document.addEventListener("DOMContentLoaded", () => {
    window.theme.syncToggles();

    const themeControlObserver = new MutationObserver(mutations => {
        if (mutations.some(mutation => mutation.addedNodes.length > 0)) {
            window.theme.syncToggles();
        }
    });
    themeControlObserver.observe(document.body, { childList: true, subtree: true });
});
window.addEventListener("storage", event => {
    if (event.key === "mhm-theme" && (event.newValue === "dark" || event.newValue === "light")) {
        window.theme.apply(event.newValue);
    }
});

window.phoneInput = {
    format: function (input) {
        let raw = input.value.trim().replace(/^00/, "+");
        const digits = raw.replace(/\D/g, "").slice(0, 15);

        if (!digits) {
            input.value = raw.startsWith("+") ? "+" : "";
            return;
        }

        const oneDigitCountryCodes = new Set(["1", "7"]);
        const commonTwoDigitCountryCodes = new Set([
            "20", "27", "30", "31", "32", "33", "34", "36", "39", "40", "41", "43", "44", "45", "46", "47", "48", "49",
            "51", "52", "53", "54", "55", "56", "57", "58", "60", "61", "62", "63", "64", "65", "66", "81", "82", "84", "86", "90", "91", "92", "93", "94", "95", "98"
        ]);
        const countryCodeLength = oneDigitCountryCodes.has(digits[0])
            ? 1
            : commonTwoDigitCountryCodes.has(digits.slice(0, 2)) ? 2 : Math.min(3, digits.length);
        const countryCode = digits.slice(0, countryCodeLength);
        const nationalNumber = digits.slice(countryCodeLength);
        const groups = nationalNumber.match(/.{1,3}/g) ?? [];
        input.value = `+${countryCode}${groups.length ? ` ${groups.join(" ")}` : ""}`;
    }
};

window.profileApi = {
    save: async function (profile) {
        const response = await fetch("/account/profile", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(profile)
        });

        const result = await response.json().catch(() => ({
            success: false,
            message: "Die Serverantwort konnte nicht verarbeitet werden."
        }));

        return result;
    }
};

window.infiniteScroll = {
    observer: null,
    observe: function (element, dotNetReference) {
        this.disconnect();
        if (!element) return;

        this.observer = new IntersectionObserver((entries) => {
            if (!entries.some(entry => entry.isIntersecting)) return;

            this.disconnect();
            dotNetReference.invokeMethodAsync("LoadMoreAsync").catch(() => { });
        }, { rootMargin: "320px 0px" });

        this.observer.observe(element);
    },
    disconnect: function () {
        this.observer?.disconnect();
        this.observer = null;
    }
};

window.pageNavigation = {
    updateReturnToTop: function () {
        const button = document.querySelector(".return-to-top");
        button?.classList.toggle("is-visible", window.scrollY > 500);
    },
    scrollToTop: function () {
        window.scrollTo({ top: 0, behavior: "smooth" });
    }
};

window.appNotifications = {
    requestPermission: async function () {
        if (!("Notification" in window)) {
            return "unsupported";
        }

        if (Notification.permission === "granted") {
            return "granted";
        }

        if (Notification.permission === "denied") {
            return "denied";
        }

        return await Notification.requestPermission();
    },
    showIfAllowed: function (title, body, url, tag) {
        if (!("Notification" in window) || Notification.permission !== "granted") {
            return false;
        }

        const notification = new Notification(title, {
            body: body,
            tag: tag,
            renotify: false
        });

        notification.onclick = function () {
            window.focus();
            if (url) {
                window.location.href = url;
            }
            notification.close();
        };

        return true;
    }
};

window.addEventListener("scroll", window.pageNavigation.updateReturnToTop, { passive: true });
document.addEventListener("DOMContentLoaded", window.pageNavigation.updateReturnToTop);
