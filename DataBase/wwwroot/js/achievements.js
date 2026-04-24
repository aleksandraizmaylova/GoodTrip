const info = document.getElementById('achievement-info');
const infoNotAuth = document.getElementById('achievement-info-not-auth');
let auth = false;

async function init() {
    let achievements = {};
    try {
        const achievedResponse = await fetch('/api/achievements', { method: 'POST' });
        auth = achievedResponse.ok;
        if (auth) {
            const result = await achievedResponse.json();
            achievements = result.achievements || {};
        }
    } catch (e) {
        console.error('Не удалось загрузить достижения:', e);
    }

    const meResponse = await fetch("/api/me");
    auth = meResponse.ok;

    for (const el of document.getElementsByClassName('achievement')) {
        const icon =  el.querySelector('.achievement-icon');
        const achievement = achievements[el.id] || { name: el.id, get: 'Не получено', percent: '0.00', description: '' };
        if (auth && achievement["get"] !== 'Не получено') {
            icon.style.filter = 'none';
        }
        el.querySelector('.achievement-text').textContent = achievement['name'];
        icon.addEventListener('click', (e) => {
            e.preventDefault();
            openInfo(achievement);
        })
    }

    await configureProfile(true);
}

(async () => {
    await init();
})();

function openInfo(achievement) {
    closeProfile();
    if (!auth) {
        infoNotAuth.style.display = 'block';
        return;
    }
    info.style.display = 'block';
    info.querySelector('#a-name').textContent = achievement['name'];
    info.querySelector('#a-description').textContent = achievement['description'];
    info.querySelector('#a-percent').textContent = `Процент выполнения: ${achievement['percent']}`;
    info.querySelector('#a-get').textContent = achievement['get'];
}

function closeInfo(){
    info.style.display = 'none';
    infoNotAuth.style.display = 'none';
}
