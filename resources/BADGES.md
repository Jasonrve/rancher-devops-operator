# Badge Customization Guide

The README.md includes several shields.io badges. Update these with your actual repository information:

## Required Updates

Replace `your-org` with your actual GitHub organization/username in:

1. **GitHub Actions Badge**
   ```
   https://img.shields.io/github/actions/workflow/status/your-org/rancher-devops-operator/build.yml
   ```

2. **Latest Release Badge**
   ```
   https://img.shields.io/github/v/tag/your-org/rancher-devops-operator
   ```

3. **GHCR Badge**
   ```
   https://img.shields.io/github/v/tag/your-org/rancher-devops-operator
   ```

4. **Update links in badges**
   - Actions link: `https://github.com/Jasonrve/rancher-devops-operator/actions`
   - Releases link: `https://github.com/Jasonrve/rancher-devops-operator/releases/latest`
   - GHCR link: `https://ghcr.io/your-org/rancher-devops-operator`

## Static Badges (Already Correct)

These badges reflect the current project status and don't need updates:
- ✅ Container Size: 57MB (measured)
- ✅ Docker arm64 Support: Enabled in Dockerfile
- ✅ Docker amd64 Support: Enabled in Dockerfile
- ✅ .NET 9.0: Project target framework

## Logo Customization

The current logo is a placeholder SVG at `./resources/images/logo.svg`

To create a custom logo:
1. Design a 128x128 image with your branding
2. Save as PNG or SVG
3. Replace `./resources/images/logo.svg` (or logo.png)
4. Update the README.md img src if needed

## Additional Badges (Optional)

Consider adding:
- Code coverage: `https://img.shields.io/codecov/c/github/your-org/rancher-devops-operator`
- License: `https://img.shields.io/github/license/your-org/rancher-devops-operator`
- Downloads: `https://img.shields.io/github/downloads/your-org/rancher-devops-operator/total`
- Stars: `https://img.shields.io/github/stars/your-org/rancher-devops-operator`

## Badge Generator

Visit https://shields.io to create custom badges with your preferred style and information.
