import dotenv from 'dotenv';

dotenv.config();

const PORT = process.env.PORT || 3000;
const API_URL = process.env.API_URL || 'http://localhost:3000';

export {
  PORT, API_URL
};