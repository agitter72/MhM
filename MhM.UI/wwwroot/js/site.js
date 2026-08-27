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