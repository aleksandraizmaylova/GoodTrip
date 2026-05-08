const form = document.getElementById("attraction-form");
const result = document.getElementById("result");

async function ensureAdminAccess() {
    try {
        const response = await fetch("/api/me");
        if (response.status === 401) {
            window.location.href = "/login.html";
            return false;
        }

        const data = await response.json();
        if (!response.ok || !data?.success) {
            throw new Error("Не удалось проверить права доступа");
        }

        if (!data.user?.isAdmin) {
            form.style.display = "none";
            result.textContent = "Доступ запрещен. Нужны права администратора.";
            result.className = "result error";
            return false;
        }

        return true;
    } catch (error) {
        form.style.display = "none";
        result.textContent = "Ошибка проверки доступа. Обновите страницу.";
        result.className = "result error";
        return false;
    }
}

async function tryReadJson(response) {
    const text = await response.text();
    if (!text) {
        return null;
    }

    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

function parseCoordinate(value) {
    return Number(value.replace(",", ".").trim());
}

form.addEventListener("submit", async (event) => {
    event.preventDefault();
    result.textContent = "Сохранение...";
    result.className = "result";

    const imageUrlsRaw = document.getElementById("imageUrls").value;
    const imageUrls = imageUrlsRaw
        .split(",")
        .map((item) => item.trim())
        .filter(Boolean);

    const categoryValue = document.getElementById("categoryId").value.trim();

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
        const response = await fetch("/api/admin/attractions", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        const data = await tryReadJson(response);

        if (!response.ok || !data?.success) {
            throw new Error(data?.error || "Не удалось добавить достопримечательность");
        }

        result.textContent = `Успешно! Добавлена запись с ID: ${data.id}`;
        result.className = "result ok";
        form.reset();
    } catch (error) {
        result.textContent = error.message;
        result.className = "result error";
    }
});

ensureAdminAccess();
