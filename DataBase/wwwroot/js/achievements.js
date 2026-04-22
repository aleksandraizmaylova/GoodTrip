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

const info = document.getElementById('achievement-panel');

for (const el of document.getElementsByClassName('achievement')) {
    const icon =  el.querySelector('.achievement-icon');
    if (achievements[el.id]["get"] !== 'Не получено') {
        icon.style.filter = 'none';
    }
    icon.addEventListener('click', (e) => {
        e.preventDefault();
        openInfo(achievements[el.id]);
    })
}

function openInfo(achievement) {
    closeProfile();
    info.style.display = "block";
    info.querySelector('#a-name').textContent = achievement['name'];
    info.querySelector('#a-description').textContent = achievement['description'];
    info.querySelector('#a-percent').textContent = `Процент выполнения: ${achievement['percent']}`;
    info.querySelector('#a-get').textContent = achievement['get'] ?? 'Не получено';
}
