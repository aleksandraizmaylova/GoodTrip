const loginForm = document.getElementById("loginForm");
loginForm.addEventListener("submit", async (event) => {
    event.preventDefault();

    const payload = {
        email: document.getElementById("email").value.trim(),
        password: document.getElementById("password").value
    };

    const response = await fetch("/api/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });

    if (!response.ok) {
        alert("Неверный email или пароль");
        return;
    }

    const result = await response.json();
    alert(`Вы вошли как ${result.user.username}`);
    const prev = document.referrer;
    console.log(prev);
    if (!prev || prev.includes('register.html')) {
        window.location.href = "/map.html";
    } else {
        window.location.href = prev;
    }
});