async function configureProfile() {
    const meResponse = await fetch("/api/me");
    if (!meResponse.ok) {
        document.getElementById("username").style.display = 'none';
        document.getElementById("email").style.display = 'none';
        document.getElementById("logoutBtn").style.display = 'none';
        return;
    }
    const meResult = await meResponse.json();
    document.getElementById("username").textContent = `Имя: ${meResult.user.username}`;
    document.getElementById("email").textContent = `Почта: ${meResult.user.email}`;
    document.getElementById("loginBtn").style.display = 'none';
}

function closeProfile() {
    document.getElementById('profile-panel').classList.remove('open');
}

function switchProfile(){
    const profileClassList = document.getElementById('profile-panel').classList;
    if (profileClassList.contains('open')) {
        profileClassList.remove('open');
    } else {
        profileClassList.add('open');
    }
}

document.getElementById("logoutBtn").addEventListener("click", async () => {
    await fetch("/api/logout", { method: "POST" });
    location.reload();
});

for (const loginBtn of document.getElementsByClassName("loginBtn")) {
    loginBtn.addEventListener("click", async () => {
        window.location.href = "/login.html";
    });
}

document.getElementById("mapBtn").addEventListener("click", async () => {
    window.location.href = "/v3.html";
});

document.getElementById("achievementsBtn").addEventListener("click", async () => {
    window.location.href = "/achievements.html";
});