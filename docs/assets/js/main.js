/**
 * SkyziBackup Website - Modern Navigation & Interactive Components
 * フェーズ2: モダンナビゲーションとインタラクティブ要素
 */

class SkyziBackupSite {
  constructor() {
    this.init();
  }

  init() {
    this.setupNavigation();
    this.setupMobileMenu();
    this.setupThemeToggle();
    this.setupAdvancedSearch();
    this.setupSmoothScroll();
    this.setupScrollEffects();
    this.setupIntersectionObserver();
    this.setupAccessibility();
    this.setupPerformanceOptimizations();
    this.setupAdvancedAnimations();
  }

  /**
   * ナビゲーションシステムの初期化
   */
  setupNavigation() {
    const currentPath = window.location.pathname;
    const navLinks = document.querySelectorAll('.nav-link');
    
    navLinks.forEach(link => {
      const href = link.getAttribute('href');
      if (href && currentPath.includes(href.replace('./', '').replace('/', ''))) {
        link.classList.add('active');
      }
      
      // ナビゲーションリンクのホバーエフェクト
      link.addEventListener('mouseenter', this.handleNavHover.bind(this));
      link.addEventListener('mouseleave', this.handleNavLeave.bind(this));
    });
  }

  /**
   * モバイルメニューの設定
   */
  setupMobileMenu() {
    const menuBtn = document.querySelector('.mobile-menu-btn');
    const mobileMenu = document.querySelector('.mobile-menu');
    const mobileNavLinks = document.querySelectorAll('.mobile-nav .nav-link');

    if (!menuBtn || !mobileMenu) return;

    // メニューボタンクリック
    menuBtn.addEventListener('click', () => {
      this.toggleMobileMenu(menuBtn, mobileMenu);
    });

    // モバイルナビリンククリック
    mobileNavLinks.forEach(link => {
      link.addEventListener('click', () => {
        this.closeMobileMenu(menuBtn, mobileMenu);
      });
    });

    // オーバーレイクリック
    mobileMenu.addEventListener('click', (e) => {
      if (e.target === mobileMenu) {
        this.closeMobileMenu(menuBtn, mobileMenu);
      }
    });

    // ESCキーでメニューを閉じる
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && mobileMenu.classList.contains('active')) {
        this.closeMobileMenu(menuBtn, mobileMenu);
      }
    });
  }

  /**
   * フェーズ3: ダークモード機能の設定
   */
  setupThemeToggle() {
    const themeToggle = document.getElementById('theme-toggle');
    if (!themeToggle) return;

    // 初期テーマの設定
    this.initializeTheme();

    // テーマ切り替えボタンのクリックイベント
    themeToggle.addEventListener('click', () => {
      this.toggleTheme();
    });

    // システムテーマ変更の監視
    if (window.matchMedia) {
      const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
      mediaQuery.addEventListener('change', () => {
        if (!localStorage.getItem('theme')) {
          this.updateThemeUI();
        }
      });
    }
  }

  /**
   * テーマの初期化
   */
  initializeTheme() {
    const savedTheme = localStorage.getItem('theme');
    const systemPrefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    
    if (savedTheme) {
      document.documentElement.setAttribute('data-theme', savedTheme);
    } else if (systemPrefersDark) {
      // システム設定を尊重するため、data-theme属性は設定しない
    } else {
      // ライトモードをデフォルトとして明示的に設定
      document.documentElement.setAttribute('data-theme', 'light');
    }
    
    this.updateThemeUI();
  }

  /**
   * テーマの切り替え
   */
  toggleTheme() {
    const currentTheme = document.documentElement.getAttribute('data-theme');
    const systemPrefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    
    let newTheme;
    
    if (currentTheme === 'dark' || (!currentTheme && systemPrefersDark)) {
      newTheme = 'light';
    } else {
      newTheme = 'dark';
    }
    
    document.documentElement.setAttribute('data-theme', newTheme);
    localStorage.setItem('theme', newTheme);
    this.updateThemeUI();
    
    // アクセシビリティ: テーマ変更の通知
    this.announceThemeChange(newTheme);
  }

  /**
   * テーマUIの更新
   */
  updateThemeUI() {
    const themeToggle = document.getElementById('theme-toggle');
    if (!themeToggle) return;
    
    const currentTheme = document.documentElement.getAttribute('data-theme');
    const systemPrefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    const isDark = currentTheme === 'dark' || (!currentTheme && systemPrefersDark);
    
    themeToggle.setAttribute('aria-label', isDark ? 'ライトモードに切り替える' : 'ダークモードに切り替える');
  }

  /**
   * テーマ変更の音声通知（アクセシビリティ）
   */
  announceThemeChange(theme) {
    const announcement = document.createElement('div');
    announcement.setAttribute('aria-live', 'polite');
    announcement.setAttribute('aria-atomic', 'true');
    announcement.className = 'sr-only';
    announcement.textContent = theme === 'dark' ? 'ダークモードに切り替えました' : 'ライトモードに切り替えました';
    
    document.body.appendChild(announcement);
    
    setTimeout(() => {
      document.body.removeChild(announcement);
    }, 1000);
  }

  /**
   * フェーズ3: 高度な検索機能の設定
   */
  setupAdvancedSearch() {
    const searchInput = document.getElementById('search-input');
    if (!searchInput) return;

    // 検索インデックスの構築
    this.buildSearchIndex();

    let searchTimeout;

    // キーボードショートカット (Ctrl+K / Cmd+K)
    document.addEventListener('keydown', (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        this.openSearchModal();
      }
      
      if (e.key === 'Escape' && this.searchModal?.classList.contains('active')) {
        this.closeSearchModal();
      }
    });

    // インクリメンタル検索
    searchInput.addEventListener('input', (e) => {
      clearTimeout(searchTimeout);
      searchTimeout = setTimeout(() => {
        this.performAdvancedSearch(e.target.value);
      }, 200);
    });

    searchInput.addEventListener('focus', () => {
      searchInput.parentElement.classList.add('focused');
      this.showSearchSuggestions();
    });

    searchInput.addEventListener('blur', () => {
      // 少し遅延してフォーカスが完全に外れたか確認
      setTimeout(() => {
        if (!searchInput.parentElement.contains(document.activeElement)) {
          searchInput.parentElement.classList.remove('focused');
          this.hideSearchSuggestions();
        }
      }, 100);
    });

    // 検索結果のキーボードナビゲーション
    searchInput.addEventListener('keydown', (e) => {
      this.handleSearchKeyboard(e);
    });
  }

  /**
   * 検索インデックスの構築
   */
  buildSearchIndex() {
    this.searchIndex = [];
    
    // ページコンテンツのインデックス化
    const contentElements = document.querySelectorAll('h1, h2, h3, h4, h5, h6, p, li');
    
    contentElements.forEach((element, index) => {
      if (element.textContent.trim()) {
        this.searchIndex.push({
          id: `content-${index}`,
          title: this.getElementTitle(element),
          content: element.textContent.trim(),
          element: element,
          type: element.tagName.toLowerCase()
        });
      }
    });

    // ナビゲーションリンクのインデックス化
    const navLinks = document.querySelectorAll('.nav-link');
    navLinks.forEach((link, index) => {
      this.searchIndex.push({
        id: `nav-${index}`,
        title: link.textContent.trim(),
        content: link.textContent.trim(),
        url: link.getAttribute('href'),
        type: 'navigation'
      });
    });
  }

  /**
   * 要素のタイトルを取得
   */
  getElementTitle(element) {
    // 見出し要素の場合はそのまま
    if (/^h[1-6]$/i.test(element.tagName)) {
      return element.textContent.trim();
    }
    
    // 前の見出しを探す
    let current = element.previousElementSibling;
    while (current) {
      if (/^h[1-6]$/i.test(current.tagName)) {
        return current.textContent.trim();
      }
      current = current.previousElementSibling;
    }
    
    return 'コンテンツ';
  }

  /**
   * スムーススクロールの設定
   */
  setupSmoothScroll() {
    document.documentElement.classList.add('smooth-scroll');

    // アンカーリンクのスムーススクロール
    document.addEventListener('click', (e) => {
      const link = e.target.closest('a[href^="#"]');
      if (!link) return;

      e.preventDefault();
      const targetId = link.getAttribute('href').substring(1);
      const targetElement = document.getElementById(targetId);

      if (targetElement) {
        const headerOffset = 80;
        const elementPosition = targetElement.offsetTop;
        const offsetPosition = elementPosition - headerOffset;

        window.scrollTo({
          top: offsetPosition,
          behavior: 'smooth'
        });
      }
    });
  }

  /**
   * スクロールエフェクトの設定
   */
  setupScrollEffects() {
    const navigation = document.querySelector('.site-navigation');
    let lastScrollY = window.scrollY;

    window.addEventListener('scroll', () => {
      const currentScrollY = window.scrollY;

      if (navigation) {
        // ナビゲーションの透明度調整
        const opacity = Math.min(1, currentScrollY / 100);
        navigation.style.setProperty('--nav-opacity', opacity);

        // スクロール方向に応じてナビゲーションの表示/非表示
        if (currentScrollY > lastScrollY && currentScrollY > 100) {
          navigation.style.transform = 'translateY(-100%)';
        } else {
          navigation.style.transform = 'translateY(0)';
        }
      }

      lastScrollY = currentScrollY;
    });
  }

  /**
   * Intersection Observer for animations
   */
  setupIntersectionObserver() {
    const observerOptions = {
      threshold: 0.1,
      rootMargin: '0px 0px -50px 0px'
    };

    const observer = new IntersectionObserver((entries) => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          entry.target.classList.add('visible');
        }
      });
    }, observerOptions);

    // フェードイン要素の監視
    document.querySelectorAll('.fade-in').forEach(el => {
      observer.observe(el);
    });

    // カード要素の監視
    document.querySelectorAll('.feature-card, .content-card').forEach(el => {
      el.classList.add('fade-in');
      observer.observe(el);
    });
  }

  /**
   * アクセシビリティの強化
   */
  setupAccessibility() {
    // フォーカストラップ（モバイルメニュー用）
    this.setupFocusTrap();
    
    // キーボードナビゲーション
    this.setupKeyboardNavigation();
    
    // ARIA属性の動的更新
    this.updateAriaAttributes();
  }

  /**
   * ナビゲーションホバーハンドラ
   */
  handleNavHover(e) {
    const link = e.currentTarget;
    link.style.transform = 'translateY(-1px)';
  }

  /**
   * ナビゲーションホバー離脱ハンドラ
   */
  handleNavLeave(e) {
    const link = e.currentTarget;
    if (!link.classList.contains('active')) {
      link.style.transform = '';
    }
  }

  /**
   * モバイルメニューの開閉
   */
  toggleMobileMenu(menuBtn, mobileMenu) {
    const isActive = mobileMenu.classList.contains('active');
    
    if (isActive) {
      this.closeMobileMenu(menuBtn, mobileMenu);
    } else {
      this.openMobileMenu(menuBtn, mobileMenu);
    }
  }

  /**
   * モバイルメニューを開く
   */
  openMobileMenu(menuBtn, mobileMenu) {
    menuBtn.classList.add('active');
    mobileMenu.classList.add('active');
    document.body.style.overflow = 'hidden';
    
    // フォーカスを最初のメニューアイテムに移動
    const firstMenuItem = mobileMenu.querySelector('.nav-link');
    if (firstMenuItem) {
      setTimeout(() => firstMenuItem.focus(), 300);
    }

    // ARIA属性の更新
    menuBtn.setAttribute('aria-expanded', 'true');
    mobileMenu.setAttribute('aria-hidden', 'false');
  }

  /**
   * モバイルメニューを閉じる
   */
  closeMobileMenu(menuBtn, mobileMenu) {
    menuBtn.classList.remove('active');
    mobileMenu.classList.remove('active');
    document.body.style.overflow = '';
    
    // フォーカスをメニューボタンに戻す
    menuBtn.focus();

    // ARIA属性の更新
    menuBtn.setAttribute('aria-expanded', 'false');
    mobileMenu.setAttribute('aria-hidden', 'true');
  }

  /**
   * 高度な検索実行
   */
  performAdvancedSearch(query) {
    if (!query.trim()) {
      this.hideSearchResults();
      return;
    }

    const results = this.searchIndex.filter(item => {
      const searchText = (item.title + ' ' + item.content).toLowerCase();
      return searchText.includes(query.toLowerCase());
    }).slice(0, 10); // 最大10件

    this.displaySearchResults(results, query);
  }

  /**
   * 検索結果の表示
   */
  displaySearchResults(results, query) {
    if (!this.searchResultsContainer) {
      this.createSearchResultsContainer();
    }

    this.searchResultsContainer.innerHTML = '';

    if (results.length === 0) {
      this.searchResultsContainer.innerHTML = `
        <div class="search-no-results">
          <p>「${query}」に関する結果が見つかりませんでした</p>
        </div>
      `;
    } else {
      results.forEach((result, index) => {
        const resultElement = this.createSearchResultElement(result, query, index);
        this.searchResultsContainer.appendChild(resultElement);
      });
    }

    this.showSearchResults();
  }

  /**
   * 検索結果要素の作成
   */
  createSearchResultElement(result, query, index) {
    const element = document.createElement('div');
    element.className = 'search-result-item';
    element.setAttribute('data-index', index);
    
    // ハイライト処理
    const highlightedTitle = this.highlightText(result.title, query);
    const highlightedContent = this.highlightText(
      result.content.substring(0, 100) + (result.content.length > 100 ? '...' : ''),
      query
    );

    element.innerHTML = `
      <div class="search-result-title">${highlightedTitle}</div>
      <div class="search-result-content">${highlightedContent}</div>
      <div class="search-result-type">${this.getTypeLabel(result.type)}</div>
    `;

    // クリックイベント
    element.addEventListener('click', () => {
      this.handleSearchResultClick(result);
    });

    return element;
  }

  /**
   * テキストのハイライト
   */
  highlightText(text, query) {
    if (!query.trim()) return text;
    
    const regex = new RegExp(`(${query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi');
    return text.replace(regex, '<mark class="search-highlight">$1</mark>');
  }

  /**
   * タイプラベルの取得
   */
  getTypeLabel(type) {
    const labels = {
      h1: 'メインタイトル',
      h2: '見出し',
      h3: 'サブ見出し',
      h4: '小見出し',
      h5: '項目',
      h6: '項目',
      p: 'テキスト',
      li: 'リスト項目',
      navigation: 'ナビゲーション'
    };
    
    return labels[type] || 'コンテンツ';
  }

  /**
   * 検索結果のクリック処理
   */
  handleSearchResultClick(result) {
    if (result.url) {
      // ナビゲーションリンクの場合
      window.location.href = result.url;
    } else if (result.element) {
      // ページ内要素の場合
      this.hideSearchResults();
      
      // スムーススクロール
      const headerOffset = 100;
      const elementPosition = result.element.offsetTop;
      const offsetPosition = elementPosition - headerOffset;

      window.scrollTo({
        top: offsetPosition,
        behavior: 'smooth'
      });

      // 要素をハイライト
      this.highlightElement(result.element);
    }
  }

  /**
   * 要素のハイライト
   */
  highlightElement(element) {
    element.classList.add('search-highlighted');
    
    setTimeout(() => {
      element.classList.remove('search-highlighted');
    }, 3000);
  }

  /**
   * 検索結果コンテナの作成
   */
  createSearchResultsContainer() {
    this.searchResultsContainer = document.createElement('div');
    this.searchResultsContainer.className = 'search-results-container';
    this.searchResultsContainer.setAttribute('aria-label', '検索結果');
    
    const searchInput = document.getElementById('search-input');
    if (searchInput) {
      searchInput.parentElement.appendChild(this.searchResultsContainer);
    }
  }

  /**
   * 検索結果の表示
   */
  showSearchResults() {
    if (this.searchResultsContainer) {
      this.searchResultsContainer.classList.add('active');
    }
  }

  /**
   * 検索結果の非表示
   */
  hideSearchResults() {
    if (this.searchResultsContainer) {
      this.searchResultsContainer.classList.remove('active');
    }
  }

  /**
   * 検索サジェストの表示
   */
  showSearchSuggestions() {
    // 人気のある検索キーワードを表示
    const suggestions = ['マニュアル', '暗号化', 'FAQ', 'プライバシー'];
    // 実装は簡略化
  }

  /**
   * 検索サジェストの非表示
   */
  hideSearchSuggestions() {
    // 実装は簡略化
  }

  /**
   * 検索モーダルを開く
   */
  openSearchModal() {
    const searchInput = document.getElementById('search-input');
    if (searchInput) {
      searchInput.focus();
    }
  }

  /**
   * 検索モーダルを閉じる
   */
  closeSearchModal() {
    const searchInput = document.getElementById('search-input');
    if (searchInput) {
      searchInput.blur();
    }
    this.hideSearchResults();
  }

  /**
   * 検索のキーボード操作
   */
  handleSearchKeyboard(e) {
    const results = this.searchResultsContainer?.querySelectorAll('.search-result-item');
    if (!results || results.length === 0) return;

    const currentIndex = parseInt(document.querySelector('.search-result-item.focused')?.getAttribute('data-index') || '-1');

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        this.focusSearchResult(results, currentIndex + 1);
        break;
      case 'ArrowUp':
        e.preventDefault();
        this.focusSearchResult(results, currentIndex - 1);
        break;
      case 'Enter':
        e.preventDefault();
        const focusedResult = document.querySelector('.search-result-item.focused');
        if (focusedResult) {
          focusedResult.click();
        }
        break;
    }
  }

  /**
   * 検索結果にフォーカス
   */
  focusSearchResult(results, index) {
    // 既存のフォーカスを削除
    results.forEach(result => result.classList.remove('focused'));

    // 範囲チェック
    if (index < 0) index = results.length - 1;
    if (index >= results.length) index = 0;

    // 新しいフォーカスを設定
    if (results[index]) {
      results[index].classList.add('focused');
      results[index].scrollIntoView({ block: 'nearest' });
    }
  }

  /**
   * フェーズ3: パフォーマンス最適化
   */
  setupPerformanceOptimizations() {
    // 画像の遅延読み込み
    this.setupLazyLoading();
    
    // リソースヒント
    this.setupResourceHints();
    
    // Critical CSS の処理
    this.optimizeCriticalCSS();
    
    // サービスワーカーの高度な制御
    this.setupAdvancedServiceWorker();
  }

  /**
   * 遅延読み込みの設定
   */
  setupLazyLoading() {
    if ('IntersectionObserver' in window) {
      const imageObserver = new IntersectionObserver((entries, observer) => {
        entries.forEach(entry => {
          if (entry.isIntersecting) {
            const img = entry.target;
            
            if (img.dataset.src) {
              img.src = img.dataset.src;
              img.removeAttribute('data-src');
            }
            
            if (img.dataset.srcset) {
              img.srcset = img.dataset.srcset;
              img.removeAttribute('data-srcset');
            }
            
            img.classList.remove('lazy');
            observer.unobserve(img);
          }
        });
      }, {
        rootMargin: '50px 0px',
        threshold: 0.01
      });

      // 既存画像への遅延読み込み適用
      document.querySelectorAll('img[data-src], img[data-srcset]').forEach(img => {
        img.classList.add('lazy');
        imageObserver.observe(img);
      });
    }
  }

  /**
   * リソースヒントの設定
   */
  setupResourceHints() {
    // 重要なリソースのプリロード
    const criticalResources = [
      { href: '/assets/css/style.css', as: 'style' },
      { href: '/assets/js/main.js', as: 'script' }
    ];

    criticalResources.forEach(resource => {
      const link = document.createElement('link');
      link.rel = 'preload';
      link.href = resource.href;
      link.as = resource.as;
      if (resource.as === 'style') {
        link.onload = function() { this.rel = 'stylesheet'; };
      }
      document.head.appendChild(link);
    });
  }

  /**
   * Critical CSS の最適化
   */
  optimizeCriticalCSS() {
    // Above-the-fold コンテンツのスタイル最適化
    const criticalStyles = `
      .page-header { display: block; }
      .site-navigation { display: block; }
      .main-content { display: block; }
    `;
    
    const style = document.createElement('style');
    style.textContent = criticalStyles;
    document.head.insertBefore(style, document.head.firstChild);
  }

  /**
   * 高度なサービスワーカー設定
   */
  setupAdvancedServiceWorker() {
    if ('serviceWorker' in navigator && window.location.protocol === 'https:') {
      navigator.serviceWorker.register('/sw.js', {
        scope: '/',
        updateViaCache: 'none'
      }).then(registration => {
        console.log('ServiceWorker registered:', registration);
        
        // 更新チェック
        registration.addEventListener('updatefound', () => {
          console.log('ServiceWorker update found');
        });
        
      }).catch(error => {
        console.log('ServiceWorker registration failed:', error);
      });
    }
  }

  /**
   * フェーズ3: 高度なアニメーション
   */
  setupAdvancedAnimations() {
    // スクロール連動アニメーション
    this.setupScrollAnimations();
    
    // ページ遷移アニメーション
    this.setupPageTransitions();
    
    // マイクロインタラクション
    this.setupMicroInteractions();
  }

  /**
   * スクロール連動アニメーション
   */
  setupScrollAnimations() {
    const animatedElements = document.querySelectorAll('.animate-on-scroll');
    
    if ('IntersectionObserver' in window) {
      const animationObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
          if (entry.isIntersecting) {
            entry.target.classList.add('animated');
          }
        });
      }, {
        threshold: 0.1,
        rootMargin: '0px 0px -50px 0px'
      });

      animatedElements.forEach(el => {
        animationObserver.observe(el);
      });
    }

    // パララックス効果
    window.addEventListener('scroll', this.throttle(() => {
      const scrolled = window.pageYOffset;
      const parallaxElements = document.querySelectorAll('.parallax');
      
      parallaxElements.forEach(element => {
        const speed = element.dataset.speed || 0.5;
        const yPos = -(scrolled * speed);
        element.style.transform = `translateY(${yPos}px)`;
      });
    }, 16));
  }

  /**
   * ページ遷移アニメーション
   */
  setupPageTransitions() {
    // SPA風のページ遷移
    document.addEventListener('click', (e) => {
      const link = e.target.closest('a[href^="/"], a[href^="./"]');
      if (!link || link.target === '_blank') return;

      e.preventDefault();
      
      // フェードアウト
      document.body.classList.add('page-transitioning');
      
      setTimeout(() => {
        window.location.href = link.href;
      }, 300);
    });
  }

  /**
   * マイクロインタラクション
   */
  setupMicroInteractions() {
    // ボタンクリック効果
    document.addEventListener('click', (e) => {
      if (e.target.matches('.btn, .nav-link, .feature-link')) {
        this.createRippleEffect(e);
      }
    });

    // ホバー効果の強化
    document.querySelectorAll('.glass-card, .feature-card').forEach(card => {
      card.addEventListener('mouseenter', (e) => {
        this.enhanceHoverEffect(e.target);
      });
    });
  }

  /**
   * リップル効果の作成
   */
  createRippleEffect(e) {
    const button = e.currentTarget;
    const ripple = document.createElement('span');
    const rect = button.getBoundingClientRect();
    const size = Math.max(rect.width, rect.height);
    const x = e.clientX - rect.left - size / 2;
    const y = e.clientY - rect.top - size / 2;
    
    ripple.style.width = ripple.style.height = size + 'px';
    ripple.style.left = x + 'px';
    ripple.style.top = y + 'px';
    ripple.classList.add('ripple');
    
    button.appendChild(ripple);
    
    setTimeout(() => {
      ripple.remove();
    }, 600);
  }

  /**
   * ホバー効果の強化
   */
  enhanceHoverEffect(element) {
    if (!element.querySelector('.hover-glow-effect')) {
      const glowEffect = document.createElement('div');
      glowEffect.className = 'hover-glow-effect';
      element.appendChild(glowEffect);
      
      setTimeout(() => {
        glowEffect.remove();
      }, 1000);
    }
  }

  /**
   * スロットル関数
   */
  throttle(func, limit) {
    let inThrottle;
    return function() {
      const args = arguments;
      const context = this;
      if (!inThrottle) {
        func.apply(context, args);
        inThrottle = true;
        setTimeout(() => inThrottle = false, limit);
      }
    };
  }

  /**
   * フォーカストラップの設定
   */
  setupFocusTrap() {
    const mobileMenu = document.querySelector('.mobile-menu');
    if (!mobileMenu) return;

    const focusableElements = mobileMenu.querySelectorAll(
      'a[href], button, textarea, input[type="text"], input[type="radio"], input[type="checkbox"], select'
    );

    const firstFocusableElement = focusableElements[0];
    const lastFocusableElement = focusableElements[focusableElements.length - 1];

    mobileMenu.addEventListener('keydown', (e) => {
      if (e.key !== 'Tab') return;

      if (e.shiftKey) {
        if (document.activeElement === firstFocusableElement) {
          lastFocusableElement.focus();
          e.preventDefault();
        }
      } else {
        if (document.activeElement === lastFocusableElement) {
          firstFocusableElement.focus();
          e.preventDefault();
        }
      }
    });
  }

  /**
   * キーボードナビゲーションの設定
   */
  setupKeyboardNavigation() {
    // メインナビゲーションのキーボード操作
    const navLinks = document.querySelectorAll('.nav-link');
    
    navLinks.forEach((link, index) => {
      link.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
          e.preventDefault();
          const nextIndex = (index + 1) % navLinks.length;
          navLinks[nextIndex].focus();
        } else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
          e.preventDefault();
          const prevIndex = (index - 1 + navLinks.length) % navLinks.length;
          navLinks[prevIndex].focus();
        }
      });
    });
  }

  /**
   * ARIA属性の動的更新
   */
  updateAriaAttributes() {
    const menuBtn = document.querySelector('.mobile-menu-btn');
    const mobileMenu = document.querySelector('.mobile-menu');

    if (menuBtn && mobileMenu) {
      menuBtn.setAttribute('aria-expanded', 'false');
      menuBtn.setAttribute('aria-controls', 'mobile-menu');
      menuBtn.setAttribute('aria-label', 'メニューを開く');
      
      mobileMenu.setAttribute('id', 'mobile-menu');
      mobileMenu.setAttribute('aria-hidden', 'true');
      mobileMenu.setAttribute('role', 'dialog');
      mobileMenu.setAttribute('aria-modal', 'true');
    }
  }

  /**
   * ページ読み込み時の初期化処理
   */
  static init() {
    return new SkyziBackupSite();
  }
}

// DOM読み込み完了後に初期化
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', SkyziBackupSite.init);
} else {
  SkyziBackupSite.init();
}

// パフォーマンス監視
if ('performance' in window) {
  window.addEventListener('load', () => {
    // Core Web Vitals の測定
    if ('PerformanceObserver' in window) {
      const observer = new PerformanceObserver((list) => {
        list.getEntries().forEach((entry) => {
          if (entry.entryType === 'largest-contentful-paint') {
            console.log('LCP:', entry.startTime);
          }
          if (entry.entryType === 'first-input') {
            console.log('FID:', entry.processingStart - entry.startTime);
          }
          if (entry.entryType === 'layout-shift') {
            console.log('CLS:', entry.value);
          }
        });
      });

      observer.observe({ entryTypes: ['largest-contentful-paint', 'first-input', 'layout-shift'] });
    }
  });
}

// エラーハンドリング
window.addEventListener('error', (e) => {
  console.error('JavaScript Error:', e.error);
});

// サービスワーカー登録（PWA対応の準備）
if ('serviceWorker' in navigator && window.location.protocol === 'https:') {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js')
      .then((registration) => {
        console.log('ServiceWorker registered:', registration);
      })
      .catch((error) => {
        console.log('ServiceWorker registration failed:', error);
      });
  });
}