import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { Toaster } from 'react-hot-toast';
import './App.css';

import { LoginPage } from './pages/Auth/Login/LoginPage';
import { RegisterPage } from './pages/Auth/Register/RegisterPage';

function App() {
  return (
    <BrowserRouter>
      {/* Cấu hình Toast Alert Toàn cục */}
      <Toaster 
        position="top-right" 
        toastOptions={{
          style: {
            background: 'var(--glass-bg)',
            color: '#fff',
            backdropFilter: 'var(--glass-blur)',
            border: '1px solid var(--glass-border)',
          }
        }} 
      />

      {/* Routes */}
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/" element={<Navigate to="/login" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
