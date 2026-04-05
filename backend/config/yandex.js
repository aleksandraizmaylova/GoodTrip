require('dotenv').config();

module.exports = {
  mapsApiKey: process.env.YANDEX_MAPS_API_KEY,
  searchApiKey: process.env.YANDEX_SEARCH_API_KEY,
  baseUrl: 'https://search-maps.yandex.ru/v1',
  geocoderUrl: 'https://geocode-maps.yandex.ru/4.x'
};