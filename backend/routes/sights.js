const express = require('express');
const router = express.Router();
const sightsController = require('../controllers/sightsController');

// GET /api/sights/search?query=музеи&lat=55.75&lng=37.61&radius=5000
router.get('/search', sightsController.search);

module.exports = router;