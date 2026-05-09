const form = document.getElementById("attraction-form");
const result = document.getElementById("result");
const categoryForm = document.getElementById("category-form");
const categorySelect = document.getElementById("categoryId");
const categoriesList = document.getElementById("categories-list");
const attractionsList = document.getElementById("attractions-list");
const reloadCategoriesButton = document.getElementById("reload-categories");
const reloadAttractionsButton = document.getElementById("reload-attractions");
const categoriesContent = document.getElementById("categories-content");
const attractionsContent = document.getElementById("attractions-content");
const toggleCategoriesButton = document.getElementById("toggle-categories");
const toggleAttractionsButton = document.getElementById("toggle-attractions");
const categorySearchInput = document.getElementById("categorySearch");
const attractionSearchInput = document.getElementById("attractionSearch");

let categoriesCache = [];
let attractionsCache = [];

async function tryReadJson(response) {
    const text = await response.text();
    if (!text) return null;
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

function parseCoordinate(value) {
    return Number(value.replace(",", ".").trim());
}

function showError(message) {
    result.textContent = message;
    result.className = "result error";
}

function showSuccess(message) {
    result.textContent = message;
    result.className = "result ok";
}

async function api(url, options = {}) {
    const response = await fetch(url, options);
    const data = await tryReadJson(response);

    if (!response.ok || !data?.success) {
        const fallbackError = response.status === 401
            ? "Требуется авторизация"
            : response.status === 403
                ? "Доступ только для администратора"
                : "Ошибка запроса";
        throw new Error(data?.error || fallbackError);
    }

    return data;
}

async function ensureAdminAccess() {
    try {
        const data = await api("/api/me");
        if (!data.user?.isAdmin) {
            throw new Error("Доступ только для администратора");
        }
        return true;
    } catch (error) {
        form.style.display = "none";
        categoryForm.style.display = "none";
        categoriesList.innerHTML = "";
        attractionsList.innerHTML = "";
        showError(error.message);
        if (error.message.includes("авторизация")) {
            window.location.href = "/login.html";
        }
        return false;
    }
}

function renderCategoryOptions(categories) {
    const options = ['<option value="">Без категории</option>'];
    for (const category of categories) {
        options.push(`<option value="${category.id}">${category.name}</option>`);
    }
    categorySelect.innerHTML = options.join("");
}

function toImageUrlString(imageUrls) {
    if (!Array.isArray(imageUrls) || imageUrls.length === 0) return "";
    return imageUrls.join(", ");
}

function filterByName(items, query) {
    const normalized = query.trim().toLowerCase();
    if (!normalized) return items;
    return items.filter((item) => (item.name || "").toLowerCase().includes(normalized));
}

function renderCategories(categories) {
    if (!categories.length) {
        categoriesList.innerHTML = '<p class="empty">Категорий пока нет</p>';
        return;
    }

    categoriesList.innerHTML = categories.map((category) => `
        <div class="card"
             data-id="${category.id}"
             data-name="${(category.name || "").replaceAll('"', "&quot;")}"
             data-icon-url="${(category.iconUrl || "").replaceAll('"', "&quot;")}"
             data-color="${(category.color || "").replaceAll('"', "&quot;")}">
            <div><strong>#${category.id}</strong> ${category.name}</div>
            <div class="small">Цвет: ${category.color || "не задан"}</div>
            <div class="small">Иконка: ${category.iconUrl || "не задана"}</div>
            <div class="actions">
                <button type="button" data-action="edit-category">Редактировать</button>
                <button type="button" data-action="delete-category" class="danger">Удалить</button>
            </div>
        </div>
    `).join("");
}

function renderAttractions(attractions) {
    if (!attractions.length) {
        attractionsList.innerHTML = '<p class="empty">Мест пока нет</p>';
        return;
    }

    attractionsList.innerHTML = attractions.map((attraction) => `
        <div class="card" data-id="${attraction.id}">
            <div><strong>#${attraction.id}</strong> ${attraction.name}</div>
            <div class="small">Категория: ${attraction.categoryName || "Без категории"}</div>
            <div class="small">Координаты: ${attraction.latitude}, ${attraction.longitude}</div>
            <div class="small">Город: ${attraction.city || "-"}</div>
            <div class="actions">
                <button type="button" data-action="edit-attraction">Редактировать</button>
                <button type="button" data-action="delete-attraction" class="danger">Удалить</button>
            </div>
        </div>
    `).join("");
}

async function loadCategories() {
    const data = await api("/api/admin/categories");
    categoriesCache = data.data || [];
    renderCategoryOptions(categoriesCache);
    renderCategories(filterByName(categoriesCache, categorySearchInput.value));
}

async function loadAttractions() {
    const data = await api("/api/admin/attractions");
    attractionsCache = data.data || [];
    renderAttractions(filterByName(attractionsCache, attractionSearchInput.value));
}

function toggleSection(contentElement, buttonElement) {
    const isHidden = contentElement.classList.toggle("hidden");
    buttonElement.textContent = isHidden ? "Развернуть" : "Свернуть";
}

form.addEventListener("submit", async (event) => {
    event.preventDefault();
    result.textContent = "Сохранение...";
    result.className = "result";

    const imageUrls = document.getElementById("imageUrls").value
        .split(",")
        .map((item) => item.trim())
        .filter(Boolean);

    const categoryValue = categorySelect.value.trim();

    const payload = {
        name: document.getElementById("name").value.trim(),
        shortDescription: document.getElementById("shortDescription").value.trim() || null,
        fullDescription: document.getElementById("fullDescription").value.trim() || null,
        categoryId: categoryValue ? Number(categoryValue) : null,
        latitude: parseCoordinate(document.getElementById("latitude").value),
        longitude: parseCoordinate(document.getElementById("longitude").value),
        address: document.getElementById("address").value.trim() || null,
        city: document.getElementById("city").value.trim() || null,
        imageUrls
    };

    try {
        const data = await api("/api/admin/attractions", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        showSuccess(`Успешно! Добавлена запись с ID: ${data.id}`);
        form.reset();
        await loadAttractions();
    } catch (error) {
        showError(error.message);
    }
});

categoryForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const payload = {
        name: document.getElementById("categoryName").value.trim(),
        iconUrl: document.getElementById("categoryIconUrl").value.trim() || null,
        color: document.getElementById("categoryColor").value.trim() || null
    };

    try {
        await api("/api/admin/categories", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });
        categoryForm.reset();
        showSuccess("Категория добавлена");
        await loadCategories();
        await loadAttractions();
    } catch (error) {
        showError(error.message);
    }
});

categoriesList.addEventListener("click", async (event) => {
    const button = event.target.closest("button");
    if (!button) return;

    const card = button.closest(".card");
    const id = Number(card?.dataset.id);
    if (!id) return;

    try {
        if (button.dataset.action === "delete-category") {
            if (!confirm("Удалить категорию?")) return;
            await api(`/api/admin/categories/${id}`, { method: "DELETE" });
            showSuccess("Категория удалена");
        }

        if (button.dataset.action === "edit-category") {
            const name = prompt("Новое название категории", card.dataset.name || "");
            if (!name) return;
            const iconUrl = prompt("Иконка URL", card.dataset.iconUrl || "");
            const color = prompt("HEX цвет", card.dataset.color || "");
            await api(`/api/admin/categories/${id}`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    name,
                    iconUrl: iconUrl || null,
                    color: color || null
                })
            });
            showSuccess("Категория обновлена");
        }

        await loadCategories();
        await loadAttractions();
    } catch (error) {
        showError(error.message);
    }
});

attractionsList.addEventListener("click", async (event) => {
    const button = event.target.closest("button");
    if (!button) return;

    const card = button.closest(".card");
    const id = Number(card?.dataset.id);
    if (!id) return;

    try {
        if (button.dataset.action === "delete-attraction") {
            if (!confirm("Удалить достопримечательность?")) return;
            await api(`/api/admin/attractions/${id}`, { method: "DELETE" });
            showSuccess("Достопримечательность удалена");
            await loadAttractions();
            return;
        }

        if (button.dataset.action === "edit-attraction") {
            const all = await api("/api/admin/attractions");
            const target = all.data.find((item) => item.id === id);
            if (!target) return;

            const name = prompt("Название", target.name);
            if (!name) return;
            const latitude = prompt("Широта", String(target.latitude));
            const longitude = prompt("Долгота", String(target.longitude));
            if (!latitude || !longitude) return;

            await api(`/api/admin/attractions/${id}`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    name,
                    shortDescription: target.shortDescription || null,
                    fullDescription: target.fullDescription || null,
                    categoryId: target.categoryId,
                    latitude: parseCoordinate(latitude),
                    longitude: parseCoordinate(longitude),
                    address: target.address || null,
                    city: target.city || null,
                    imageUrls: toImageUrlString(target.imageUrls)
                        .split(",")
                        .map((item) => item.trim())
                        .filter(Boolean)
                })
            });
            showSuccess("Достопримечательность обновлена");
            await loadAttractions();
        }
    } catch (error) {
        showError(error.message);
    }
});

reloadCategoriesButton.addEventListener("click", async () => {
    try {
        await loadCategories();
        showSuccess("Категории обновлены");
    } catch (error) {
        showError(error.message);
    }
});

reloadAttractionsButton.addEventListener("click", async () => {
    try {
        await loadAttractions();
        showSuccess("Список мест обновлен");
    } catch (error) {
        showError(error.message);
    }
});

toggleCategoriesButton.addEventListener("click", () => {
    toggleSection(categoriesContent, toggleCategoriesButton);
});

toggleAttractionsButton.addEventListener("click", () => {
    toggleSection(attractionsContent, toggleAttractionsButton);
});

categorySearchInput.addEventListener("input", () => {
    renderCategories(filterByName(categoriesCache, categorySearchInput.value));
});

attractionSearchInput.addEventListener("input", () => {
    renderAttractions(filterByName(attractionsCache, attractionSearchInput.value));
});

(async () => {
    const ok = await ensureAdminAccess();
    if (!ok) return;
    await loadCategories();
    await loadAttractions();
})();
