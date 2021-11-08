FROM ruby:2.7.3

# throw errors if Gemfile has been modified since Gemfile.lock
#RUN bundle config --global frozen 1

WORKDIR /usr/src/app

COPY Gemfile Gemfile.lock ./

RUN gem install http_parser.rb -v '0.6.0' --source 'https://rubygems.org/'
RUN bundle install

COPY . .
