const registerForm = document.getElementById("registerForm");
registerForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    const payload = {
        username: document.getElementById("username").value.trim(),
        email: document.getElementById("email").value.trim(),
        password: document.getElementById("password").value
    };

    const response = await fetch("/api/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });

    const result = await response.json().catch(() => ({}));
    if (!response.ok) {
        alert(result.error || "Ошибка регистрации");
        return;
    }

    const loginResponse = await fetch("/api/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            email: payload.email,
            password: payload.password
        })
    });

    if (!loginResponse.ok) {
        alert("Регистрация успешна, но вход не выполнен. Войдите вручную.");
        window.location.href = "/login.html";
        return;
    }

    window.location.href = "/map.html";
});