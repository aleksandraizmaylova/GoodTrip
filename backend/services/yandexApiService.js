class YandexApiService {
    // Тестовые достопримечательности (Москва + СПб)
    #mockAttractions = [
      {
        id: 'mock-1',
        name: 'Государственный исторический музей',
        description: 'Крупнейший национальный исторический музей России',
        coordinates: { lat: 55.7558, lng: 37.6211 },
        address: 'Москва, Красная площадь, 1',
        category: 'Музей',
        rating: 4.8
      },
      {
        id: 'mock-2',
        name: 'Третьяковская галерея',
        description: 'Художественный музей с коллекцией русского искусства',
        coordinates: { lat: 55.7414, lng: 37.6189 },
        address: 'Москва, Лаврушинский пер., 10',
        category: 'Музей',
        rating: 4.9
      },
      {
        id: 'mock-3',
        name: 'Храм Василия Блаженного',
        description: 'Православный храм на Красной площади',
        coordinates: { lat: 55.7525, lng: 37.6231 },
        address: 'Москва, Красная площадь',
        category: 'Достопримечательность',
        rating: 4.9
      },
      {
        id: 'mock-4',
        name: 'Эрмитаж',
        description: 'Один из крупнейших художественных музеев мира',
        coordinates: { lat: 59.9398, lng: 30.3146 },
        address: 'Санкт-Петербург, Дворцовая наб., 34',
        category: 'Музей',
        rating: 4.9
      },
      {
        id: 'mock-5',
        name: 'Петергоф',
        description: 'Дворцово-парковый ансамбль с фонтанами',
        coordinates: { lat: 59.8847, lng: 29.9089 },
        address: 'Санкт-Петербург, г. Петергоф',
        category: 'Парк',
        rating: 4.8
      }
    ];
  
    async searchAttractions(text, coordinates = null, radius = 1000) {
      console.log(`🔍 [MOCK] Поиск: "${text}", координаты:`, coordinates);
      
      await new Promise(resolve => setTimeout(resolve, 300));
      
      const query = text.toLowerCase();
      let results = this.#mockAttractions.filter(attraction => 
        attraction.name.toLowerCase().includes(query) ||
        attraction.category.toLowerCase().includes(query) ||
        attraction.description.toLowerCase().includes(query)
      );
      
      if (results.length === 0) {
        console.log('⚠️ [MOCK] Ничего не найдено, возвращаем все данные');
        results = this.#mockAttractions;
      }
      
      if (coordinates) {
        results = results.filter(attraction => {
          const distance = this.#calculateDistance(
            coordinates.lat, coordinates.lng,
            attraction.coordinates.lat, attraction.coordinates.lng
          );
          return distance <= radius;
        });
      }
      
      console.log(`✅ [MOCK] Найдено ${results.length} объектов`);
      return results;
    }
  
    #calculateDistance(lat1, lon1, lat2, lon2) {
      const R = 6371;
      const dLat = (lat2 - lat1) * Math.PI / 180;
      const dLon = (lon2 - lon1) * Math.PI / 180;
      const a = 
        Math.sin(dLat/2) * Math.sin(dLat/2) +
        Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) * 
        Math.sin(dLon/2) * Math.sin(dLon/2);
      const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1-a));
      return R * c * 1000;
    }
  }
  
  module.exports = new YandexApiService();