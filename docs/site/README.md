# Site web Riders Mirroring

Site statique servi par GitHub Pages à l'URL
**https://riders-mirroring.github.io/**.

## Pages

| Fichier | URL | Rôle |
| --- | --- | --- |
| `index.html` | `/` | Accueil — présentation + features + CTA download |
| `download.html` | `/download.html` | Téléchargement MSI + étapes d'install |
| `changelog.html` | `/changelog.html` | Historique des versions |

## Assets

- `style.css` — palette or/bronze cohérente avec le branding WPF
- `favicon.svg` — icône R sur blason doré
- `screenshot-hub.png` — capture d'écran du hub (à ajouter avant déploiement)

## Déploiement

Automatique via GitHub Actions :
- Trigger : push sur `main` qui touche `docs/site/**`
- Workflow : `.github/workflows/pages.yml`
- Output : branche `gh-pages` servie par GitHub Pages

Pour mettre à jour le site manuellement :
1. Édite les fichiers HTML/CSS dans `docs/site/`
2. Commit + push sur `main`
3. Le site est redéployé en ~30 secondes

## Mirror local des binaires

Le workflow `release.yml` copie automatiquement le MSI dans
`docs/site/downloads/<tag>.msi` (avec son `.sha256`) à chaque release
taggée. Cela permet au site d'avoir un lien miroir même si GitHub
Releases est en panne.

## Personnalisation

Pour adapter le site à ton branding :
- Palette : variables CSS en haut de `style.css`
- Liens GitHub : remplace `meronkevin95610-crypto/riders-mirroring` par ton
  vrai username/repo (3 occurrences dans chaque HTML)
- Screenshots : remplace `screenshot-hub.png` par ta propre capture
