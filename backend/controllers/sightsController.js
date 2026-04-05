const yandexApiService = require('../services/yandexApiService');

class SightsController {
  async search(req, res) {
    try {
      const { query, lat, lng, radius } = req.query;

      if (!query) {
        return res.status(400).json({ error: 'Необходим поисковый запрос' });
      }

      const coordinates = (lat && lng) ? { lat: parseFloat(lat), lng: parseFloat(lng) } : null;
      const searchRadius = radius ? parseInt(radius) : 1000;

      const attractions = await yandexApiService.searchAttractions(
        query,
        coordinates,
        searchRadius
      );

      res.json({
        success: true,
        count: attractions.length,
        data: attractions
      });

    } catch (error) {
      console.error('Search error:', error);
      res.status(500).json({ 
        success: false, 
        error: error.message 
      });
    }
  }
}

module.exports = new SightsController();