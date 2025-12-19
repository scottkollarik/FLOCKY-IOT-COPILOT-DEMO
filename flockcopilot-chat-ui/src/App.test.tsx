import { render, screen } from '@testing-library/react';
import App from './App';
import { test, expect } from 'vitest';

test('renders learn react link', () => {
  render(<App />);
  expect(screen.getByRole('heading', { name: /FlockCopilot Diagnostic Agent/i })).toBeInTheDocument();
});
