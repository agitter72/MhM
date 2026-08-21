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