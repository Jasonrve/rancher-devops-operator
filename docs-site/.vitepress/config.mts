import { defineConfig } from 'vitepress'

const base = process.env.VITEPRESS_BASE ?? '/'

export default defineConfig({
  lang: 'en-US',
  title: 'Rancher DevOps Operator',
  description: 'Declarative documentation for the Rancher DevOps Operator',
  lastUpdated: true,
  cleanUrls: true,
  base,
  themeConfig: {
    logo: '/logo.png',
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Getting Started', link: '/guide/getting-started' },
      { text: 'Project CRD', link: '/reference/project-crd' },
      { text: 'Installation', link: '/reference/installation' },
      { text: 'Troubleshooting', link: '/guide/troubleshooting' }
    ],
    sidebar: {
      '/guide/': [
        {
          text: 'Guide',
          items: [
            { text: 'Getting Started', link: '/guide/getting-started' },
            { text: 'Troubleshooting', link: '/guide/troubleshooting' }
          ]
        }
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Installation', link: '/reference/installation' },
            { text: 'Project CRD', link: '/reference/project-crd' }
          ]
        }
      ]
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/Jasonrve/rancher-devops-operator' }
    ],
    footer: {
      message: 'Built from the Rancher DevOps Operator source tree.',
      copyright: 'Copyright © 2026 Jasonrve'
    },
    editLink: {
      pattern: 'https://github.com/Jasonrve/rancher-devops-operator/edit/main/docs-site/:path',
      text: 'Edit this page on GitHub'
    },
    lastUpdatedText: 'Last updated'
  },
  markdown: {
    theme: {
      light: 'vitesse-light',
      dark: 'vitesse-dark'
    }
  }
})
