import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import eslintConfigPrettier from 'eslint-config-prettier';

export default tseslint.config(
  // Anchored with a leading **/ ON PURPOSE — see the identical note in
  // web/timesheet/eslint.config.mjs. ESLint resolves flat-config `ignores` against the CWD,
  // not the config file's directory. `npm run lint` runs `eslint .` from inside this app, but
  // the root lint-staged task invokes this same config from the REPO ROOT, where bare patterns
  // would not match. These globs match from either cwd so the two entry points cannot disagree.
  {
    ignores: ['**/dist/**', '**/node_modules/**', '**/coverage/**'],
  },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, ...tseslint.configs.strict, ...tseslint.configs.stylistic],
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      ...reactRefresh.configs.vite.rules,
      '@typescript-eslint/consistent-type-imports': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
    },
    languageOptions: {
      ecmaVersion: 2022,
    },
  },
  eslintConfigPrettier,
);
