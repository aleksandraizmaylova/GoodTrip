ymaps.ready(init);

const categoryToEng = { "музей": "museum", "парк": "park", "памятник": "monument", "достопримечательность": "attraction", "храм": "temple" };
const bgColors = { "музей": "#ef4444", "парк": "#10b981", "памятник": "#eab308", "храм": "#f97316", "default": "#3b82f6" };
const emojis = { "музей": "🏛️", "парк": "🌳", "памятник": "🗿", "храм": "⛪", "default": "📍" };

let visitedPlaces = [];
let favoritePlaces = [];
let currentPlaceId = null;
let mapPlacemarks = {};

const allAvailableCats = ["museum", "park", "monument", "temple"];
let activeCategories = new Set(allAvailableCats);
let filterFav = false;
let filterVis = false;

function toggleCategory(cat) {
    if (activeCategories.has(cat)) activeCategories.delete(cat);
    else activeCategories.add(cat);
    updateFiltersUI();
}

function toggleAllCategories() {
    if (activeCategories.size === allAvailableCats.length) {
        activeCategories.clear();
    } else {
        allAvailableCats.forEach(c => activeCategories.add(c));
    }
    updateFiltersUI();
}

function toggleStatus(status) {
    if (status === 'favorites') filterFav = !filterFav;
    if (status === 'visited') filterVis = !filterVis;
    updateFiltersUI();
}

function updateFiltersUI() {
    const allBtn = document.getElementById('filter-all');
    if (activeCategories.size === allAvailableCats.length) allBtn.classList.add('active');
    else allBtn.classList.remove('active');

    document.querySelectorAll('.filter-cat').forEach(btn => {
        const cat = btn.getAttribute('data-cat');
        if (activeCategories.has(cat)) btn.classList.add('active');
        else btn.classList.remove('active');
    });

    document.getElementById('filter-fav').classList.toggle('active', filterFav);
    document.getElementById('filter-vis').classList.toggle('active', filterVis);

    updateMarkersOnMap();
}

function updatePanelUI() {
    const isVisited = visitedPlaces.includes(currentPlaceId);
    const isFavorite = favoritePlaces.includes(currentPlaceId);

    const visitedBadge = document.getElementById('panel-visited-status');
    const toggleVisitedBtn = document.getElementById('btn-toggle-visited');
    const favBadge = document.getElementById('panel-favorite-status');
    const toggleFavBtn = document.getElementById('btn-toggle-favorite');

    if (isVisited) {
        visitedBadge.textContent = '✅ Посещено'; visitedBadge.className = 'status-badge status-visited';
        toggleVisitedBtn.textContent = 'Убрать из посещенных'; toggleVisitedBtn.className = 'action-btn btn-mark-unvisited';
        toggleFavBtn.style.display = 'none'; favBadge.className = 'status-badge status-fav-inactive';
    } else {
        visitedBadge.textContent = 'Не посещено'; visitedBadge.className = 'status-badge status-unvisited';
        toggleVisitedBtn.textContent = '✅ Отметить как посещенное'; toggleVisitedBtn.className = 'action-btn btn-mark-visited';
        toggleFavBtn.style.display = 'flex';

        if (isFavorite) {
            favBadge.textContent = '❤️ В планах'; favBadge.className = 'status-badge status-fav-active';
            toggleFavBtn.textContent = '❤️ Убрать из планов'; toggleFavBtn.className = 'action-btn btn-fav-active';
        } else {
            favBadge.className = 'status-badge status-fav-inactive';
            toggleFavBtn.textContent = '🤍 Хочу посетить'; toggleFavBtn.className = 'action-btn btn-fav-inactive';
        }
    }
}

function updateMarkersOnMap() {
    for (let id in mapPlacemarks) {
        const placemark = mapPlacemarks[id].instance;
        const placeData = mapPlacemarks[id].rawData;

        const isVis = visitedPlaces.includes(id);
        const isFav = favoritePlaces.includes(id);

        let statusClass = '';
        let badgeHtml = '';

        if (isVis) {
            statusClass = 'marker-visited';
            badgeHtml = '<div class="marker-badge">✅</div>';
        } else if (isFav) {
            statusClass = 'marker-favorite';
            badgeHtml = '<div class="marker-badge">❤️</div>';
        }

        placemark.properties.set('statusClass', statusClass);
        placemark.properties.set('badgeHtml', badgeHtml);

        let isVisible = true;
        const ruCat = (placeData.category || "").toLowerCase().trim();
        const cat = categoryToEng[ruCat] || "default";

        if (cat !== "default" && !activeCategories.has(cat)) isVisible = false;
        if (cat === "default" && activeCategories.size === 0) isVisible = false;
        if (filterFav && !isFav) isVisible = false;
        if (filterVis && !isVis) isVisible = false;

        placemark.options.set('visible', isVisible);

        if (!isVisible && currentPlaceId === id) {
            closePanel();
        }
    }
}

async function toggleVisited() {
    if (!currentPlaceId) return;
    const isVisited = visitedPlaces.includes(currentPlaceId);
    const nextStatus = isVisited ? "not_visited" : "visited";

    const response = await fetch(`/api/attractions/${currentPlaceId}/status`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ status: nextStatus })
    });
    if (!response.ok) return;

    if (isVisited) {
        visitedPlaces = visitedPlaces.filter(x => x !== currentPlaceId);
    } else {
        visitedPlaces.push(currentPlaceId);
        favoritePlaces = favoritePlaces.filter(x => x !== currentPlaceId);
    }
    updatePanelUI();
    updateMarkersOnMap();
}

async function toggleFavorite() {
    if (!currentPlaceId) return;
    const isFavorite = favoritePlaces.includes(currentPlaceId);
    const nextStatus = isFavorite ? "not_visited" : "planned";

    const response = await fetch(`/api/attractions/${currentPlaceId}/status`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ status: nextStatus })
    });
    if (!response.ok) return;

    if (isFavorite) {
        favoritePlaces = favoritePlaces.filter(x => x !== currentPlaceId);
    } else {
        favoritePlaces.push(currentPlaceId);
        visitedPlaces = visitedPlaces.filter(x => x !== currentPlaceId);
    }
    updatePanelUI();
    updateMarkersOnMap();
}

function openPanel(place) {
    currentPlaceId = place.id;
    document.getElementById('panel-title').innerText = place.name;
    document.getElementById('panel-desc').innerText = place.description;
    document.getElementById('panel-address').innerHTML = `📍 ${place.address}`;
    document.getElementById('panel-category').innerText = place.category || 'Место';
    document.getElementById('panel-rating').innerText = `★ ${place.rating ? place.rating.toFixed(1) : 'Нет оценки'}`;

    updatePanelUI();
    document.getElementById('side-panel').classList.add('open');
}

function closePanel() {
    document.getElementById('side-panel').classList.remove('open');
    currentPlaceId = null;
}

async function init() {
    await configureProfile();
    try {
        const statusResponse = await fetch("/api/user/attraction-status");
        if (statusResponse.ok) {
            const statusResult = await statusResponse.json();
            visitedPlaces = (statusResult.visited || []).map(x => x.toString());
            favoritePlaces = (statusResult.planned || []).map(x => x.toString());
        }
    } catch (e) {
        console.error("Не удалось загрузить статусы пользователя:", e);
    }

    var myMap = new ymaps.Map("map", {
        center: [57.5, 34.0],
        zoom: 5,
        controls: ['geolocationControl']
    });

    var zoomControl = new ymaps.control.ZoomControl({ options: { position: { right: 10, top: 100 } } });
    myMap.controls.add(zoomControl);
    myMap.events.add('click', function () {
        closePanel();
        closeProfile();
    });

    const CustomMarkerLayout = ymaps.templateLayoutFactory.createClass(
        '<div class="custom-marker $[properties.statusClass]" ' +
        'style="background-color: $[properties.bgColor]; ' +
        '{% if properties.iconUrl %}background-image: url(\'$[properties.iconUrl]\');{% endif %}">' +
        '$[properties.emoji]' +
        '$[properties.badgeHtml]' +
        '</div>'
    );

    let result;
    try {
        const response = await fetch("/api/sights/search");
        if (response.status === 401) {
            window.location.href = "/login.html";
            return;
        }
        result = await response.json();
    } catch (e) {
        console.error("Не удалось загрузить данные для карты:", e);
        return;
    }

    if (result && result.success && result.data) {
        result.data.forEach(place => {
            const catStr = (place.category || "").toLowerCase().trim();
            const engName = categoryToEng[catStr] || "default";
            const imageUrl = `logo_${engName}.png`;
            const bgColor = bgColors[engName] || bgColors["default"];

            const img = new Image();

            const buildPlacemark = (finalImageUrl, emoji) => {
                var placemark = new ymaps.Placemark(
                    [place.coordinates.lat, place.coordinates.lng],
                    {
                        hintContent: place.name, iconUrl: finalImageUrl, bgColor: bgColor, emoji: emoji,
                        statusClass: '', badgeHtml: ''
                    },
                    { iconLayout: CustomMarkerLayout, iconShape: { type: 'Circle', coordinates: [0, 0], radius: 22 } }
                );

                placemark.events.add('click', function () { openPanel(place); });
                myMap.geoObjects.add(placemark);
                mapPlacemarks[place.id] = { instance: placemark, rawData: place };
            };

            img.onload = function() { buildPlacemark(imageUrl, ""); };
            img.onerror = function() { const fallbackEmoji = emojis[engName] || emojis["default"]; buildPlacemark("", fallbackEmoji); };
            img.src = imageUrl;
        });

        setTimeout(updateMarkersOnMap, 300);
    }
}