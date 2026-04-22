const info = document.getElementById('achievement-info');
const infoNotAuth = document.getElementById('achievement-info-not-auth');
let auth = false;

async function init() {
    // const achievedResponse = await fetch('/api/achievements', {method: 'POST'});
    // const result = await achievedResponse.json();
    // const achieved = result.achieved;
    const achievements = {
        "achievement1": {
            "id": "achievement1",
            'name': 'Первый шаг',
            'get': 'Получено: 22.04.2026',
            "percent": '52.67',
            'description': 'Зарегистрируйтесь на сайте'
        },
        "achievement2": {
            "id": "achievement2",
            'name': 'Святой',
            'get': 'Не получено',
            "percent": '6.66',
            'description': 'Посетите 5 храмов'
        },
        "achievement3": {
            "id": "achievement3",
            'name': 'Знаток',
            'get': 'Получено: 22.04.2026',
            "percent": '14.88',
            'description': 'Предложите свою достопримечательность'
        }
    };

    const meResponse = await fetch("/api/me");
    auth = meResponse.ok;

    for (const el of document.getElementsByClassName('achievement')) {
        const icon =  el.querySelector('.achievement-icon');
        const achievement = achievements[el.id];
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
